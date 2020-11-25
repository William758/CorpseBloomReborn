using BepInEx;
using BepInEx.Configuration;
using MiniRpcLib;
using MiniRpcLib.Action;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using R2API;
using R2API.Utils;
using RoR2;
using RoR2.UI;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace TPDespair.CorpseBloomReborn
{
    [BepInDependency(R2API.R2API.PluginGUID)]
    [BepInDependency(MiniRpcPlugin.Dependency)]
    [BepInPlugin(ModGuid, ModName, ModVer)]
    [R2APISubmoduleDependency(nameof(LanguageAPI))]

    public class CorpseBloomRebornPlugin : BaseUnityPlugin
    {
        public const string ModVer = "1.0.0";
        public const string ModName = "CorpseBloomReborn";
        public const string ModGuid = "com.TPDespair.CorpseBloomReborn";



        private float currentReserve = 0f;
        private float maxReserve = -1f;
        private NetworkInstanceId netId;

        private GameObject reserveRect;
        private GameObject reserveBar;
        private HealthBar hpBar;



        private ReserveAmountMessage defaultAmountMessage = new ReserveAmountMessage(0f, -1f);

        private Dictionary<NetworkInstanceId, ReserveAmountMessage> ReserveData = new Dictionary<NetworkInstanceId, ReserveAmountMessage>();
        private Dictionary<NetworkInstanceId, ReserveTargetMessage> ReserveTarget = new Dictionary<NetworkInstanceId, ReserveTargetMessage>();



        public static IRpcAction<ReserveAmountMessage> UpdateReserveAmountCommand { get; set; }
        public static IRpcAction<ReserveTargetMessage> UpdateReserveTargetCommand { get; set; }



        public static ConfigEntry<bool> HealBeforeReserve { get; set; }
        public static ConfigEntry<bool> HealReserveFull { get; set; }
        public static ConfigEntry<float> BaseAbsorbMult { get; set; }
        public static ConfigEntry<float> StackAbsorbMult { get; set; }
        public static ConfigEntry<float> BaseHealMult { get; set; }
        public static ConfigEntry<float> StackHealMult { get; set; }
        public static ConfigEntry<float> BaseHealthReserve { get; set; }
        public static ConfigEntry<float> StackHealthReserve { get; set; }
        public static ConfigEntry<float> BaseMaxUsageRate { get; set; }
        public static ConfigEntry<float> StackMaxUsageRate { get; set; }
        public static ConfigEntry<float> BaseMinUsageRate { get; set; }
        public static ConfigEntry<float> StackMinUsageRate { get; set; }



        public CorpseBloomRebornPlugin()
        {
            var miniRpc = MiniRpc.CreateInstance(ModGuid);

            UpdateReserveAmountCommand = miniRpc.RegisterAction(Target.Client, (NetworkUser user, ReserveAmountMessage ram) =>
            {
                if (ram != null)
                {
                    currentReserve = ram.currentReserve;
                    maxReserve = ram.maxReserve;
                }
            });
            UpdateReserveTargetCommand = miniRpc.RegisterAction(Target.Server, (NetworkUser user, ReserveTargetMessage rtm) =>
            {
                if (rtm != null)
                {
                    if (!ReserveTarget.ContainsKey(user.netId))
                    {
                        ReserveTarget.Add(user.netId, new ReserveTargetMessage(rtm.netId));
                    }
                    else
                    {
                        ReserveTarget[user.netId].netId = rtm.netId;
                    }
                }
            });
        }



        private void ConfigSetup()
        {
            HealBeforeReserve = Config.Bind<bool>(
                "General",
                "HealBeforeReserve", true,
                "If incoming healing should apply to health before going into reserve."
            );
            HealReserveFull = Config.Bind<bool>(
                "General",
                "HealReserveFull", true,
                "If incoming healing should apply to health after reserve is full."
            );
            BaseAbsorbMult = Config.Bind<float>(
                "General",
                "BaseAbsorbMult", 0f,
                "Base increased reserve absorption. Increases effectiveness of healing going into reserve."
            );
            StackAbsorbMult = Config.Bind<float>(
                "General",
                "StackAbsorbMult", 0f,
                "Stack increased reserve absorption. Increases effectiveness of healing going into reserve."
            );
            BaseHealMult = Config.Bind<float>(
                "General",
                "BaseHealMult", 0f,
                "Base increased healing effectiveness. Old CB only effected healing going into reserve. This setting effects actual healing!"
            );
            StackHealMult = Config.Bind<float>(
                "General",
                "StackHealMult", 0f,
                "Stack increased healing effectiveness. Old CB only effected healing going into reserve. This setting effects actual healing!"
            );
            BaseHealthReserve = Config.Bind<float>(
                "General",
                "BaseHealthReserve", 0.5f,
                "Base reserve gained from health."
            );
            StackHealthReserve = Config.Bind<float>(
                "General",
                "StackHealthReserve", 0.25f,
                "Stack reserve gained from health."
            );
            BaseMaxUsageRate = Config.Bind<float>(
                "General",
                "BaseMaxUsageRate", 0.10f,
                "Base maximum healing output from reserve per second."
            );
            StackMaxUsageRate = Config.Bind<float>(
                "General",
                "StackMaxUsageRate", 0f,
                "Stack maximum healing output from reserve per second."
            );
            BaseMinUsageRate = Config.Bind<float>(
                "General",
                "BaseMinUsageRate", 0.025f,
                "Base minimum healing output from reserve per second. Set to 0 to disable reserve decay."
            );
            StackMinUsageRate = Config.Bind<float>(
                "General",
                "StackMinUsageRate", 0f,
                "Stack minimum healing output from reserve per second. Set to 0 to disable reserve decay."
            );

            if (BaseHealthReserve.Value < 0.1f) BaseHealthReserve.Value = 0.1f;

            if (BaseMaxUsageRate.Value < 0.001f)
            {
                BaseMaxUsageRate.Value = 0.001f;
                if (StackMaxUsageRate.Value < 0f) StackMaxUsageRate.Value = 0;
            }
            if (BaseMinUsageRate.Value < 0f)
            {
                BaseMinUsageRate.Value = 0f;
                if (StackMinUsageRate.Value < 0f) StackMinUsageRate.Value = 0;
            }

            if (BaseMinUsageRate.Value > BaseMaxUsageRate.Value) BaseMinUsageRate.Value = BaseMaxUsageRate.Value;
        }



        private int HealthComponentItemCount(HealthComponent healthComponent, ItemIndex itemIndex)
        {
            int count = 0;

            if (healthComponent.body != null)
            {
                Inventory inventory = healthComponent.body.inventory;
                if (inventory) count = inventory.GetItemCount(itemIndex);
            }

            return count;
        }

        private float GetHealScaleFactor(HealthComponent healthComponent)
        {
            float factor = 1f + HealthComponentItemCount(healthComponent, ItemIndex.IncreaseHealing);
            if (healthComponent.body.teamComponent.teamIndex == TeamIndex.Player && Run.instance.selectedDifficulty >= DifficultyIndex.Eclipse5) factor *= 0.5f;
            return factor;
        }

        private float GetMinReserveRate(HealthComponent healthComponent)
        {
            int count = HealthComponentItemCount(healthComponent, ItemIndex.RepeatHeal);
            return Mathf.Max(0f, BaseMinUsageRate.Value + ((count - 1) * StackMinUsageRate.Value));
        }

        private float GetMaxReserveRate(HealthComponent healthComponent)
        {
            int count = HealthComponentItemCount(healthComponent, ItemIndex.RepeatHeal);
            float min = Mathf.Max(0f, BaseMinUsageRate.Value + ((count - 1) * StackMinUsageRate.Value));
            return Mathf.Max(0.001f, min, BaseMaxUsageRate.Value + ((count - 1) * StackMaxUsageRate.Value));
        }



        private void HealthUIAwakeHook()
        {
            On.RoR2.UI.HUD.Awake += (self, orig) =>
            {
                self(orig);

                InitializeReserveUI();
                reserveRect.transform.SetParent(orig.healthBar.transform, false);
                hpBar = orig.healthBar;
            };
        }

        private void InitializeReserveUI()
        {
            reserveRect = new GameObject();
            reserveRect.name = "ReserveRect";
            reserveRect.AddComponent<RectTransform>();
            reserveRect.GetComponent<RectTransform>().position = new Vector3(0f, 0f);
            reserveRect.GetComponent<RectTransform>().anchoredPosition = new Vector2(210, 0);
            reserveRect.GetComponent<RectTransform>().anchorMin = new Vector2(0, 0);
            reserveRect.GetComponent<RectTransform>().anchorMax = new Vector2(0, 0);
            reserveRect.GetComponent<RectTransform>().offsetMin = new Vector2(210, 0);
            reserveRect.GetComponent<RectTransform>().offsetMax = new Vector2(210, 10);
            reserveRect.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 10);
            reserveRect.GetComponent<RectTransform>().pivot = new Vector2(0, 0);

            reserveBar = new GameObject();
            reserveBar.name = "ReserveBar";
            reserveBar.transform.SetParent(reserveRect.GetComponent<RectTransform>().transform);
            reserveBar.AddComponent<RectTransform>().pivot = new Vector2(0.5f, 1.0f);
            reserveBar.GetComponent<RectTransform>().sizeDelta = reserveRect.GetComponent<RectTransform>().sizeDelta;
            reserveBar.AddComponent<Image>().color = new Color(0.625f, 0.25f, 1f, 0.65f);
        }

        private void HealthUIUpdateHook()
        {
            On.RoR2.UI.HUD.Update += (self, orig) =>
            {
                self(orig);

                UpdateReserveUI(orig);
            };
        }

        private void UpdateReserveUI(HUD hud)
        {
            if (hud != null) hpBar = hud.healthBar;

            if (hpBar != null && hpBar.source)
            {
                HealthComponent healthComponent = hpBar.source;

                if (healthComponent.body.netId != null && netId != healthComponent.body.netId)
                {
                    netId = healthComponent.body.netId;
                    currentReserve = 0f;
                    maxReserve = -1f;

                    // client tell server to now send data for target netId
                    if (!NetworkServer.active)
                    {
                        ReserveTargetMessage reserveTargetMessage = new ReserveTargetMessage(netId);
                        UpdateReserveTargetCommand.Invoke(reserveTargetMessage);
                    }
                }

                if (healthComponent.body.inventory != null)
                {
                    if (healthComponent.body.inventory.GetItemCount(ItemIndex.RepeatHeal) != 0)
                    {
                        //Debug.LogWarning(currentReserve + "-" + maxReserve);
                        float mult = 1f / healthComponent.body.cursePenalty;
                        mult *= healthComponent.fullHealth / healthComponent.fullCombinedHealth;
                        float percentReserve = Mathf.Clamp(currentReserve / maxReserve, 0f, 1f);
                        float dispValue = -0.5f + percentReserve * mult;
                        reserveBar.GetComponent<RectTransform>().anchorMax = new Vector2(dispValue, 0.5f);
                        if (reserveRect.activeSelf == false) reserveRect.SetActive(true);
                    }
                    else
                    {
                        if (reserveRect.activeSelf == true) reserveRect.SetActive(false);
                    }
                }
            }
        }



        private void RecalculateStatsHook()
        {
            On.RoR2.CharacterBody.RecalculateStats += (self, orig) =>
            {
                self(orig);

                CalculateReserveCapacity(orig.healthComponent);
            };
        }

        private void CalculateReserveCapacity(HealthComponent healthComponent)
        {
            if (NetworkServer.active)
            {
                if (healthComponent.body != null)
                {
                    int count = HealthComponentItemCount(healthComponent, ItemIndex.RepeatHeal);

                    if (count > 0)
                    {
                        CharacterBody charBody = healthComponent.body;
                        float maxReserve = healthComponent.fullHealth * Mathf.Max(0.1f, BaseHealthReserve.Value + StackHealthReserve.Value * (count - 1));
                        bool update = false;

                        if (!ReserveData.ContainsKey(charBody.netId))
                        {
                            ReserveData.Add(charBody.netId, new ReserveAmountMessage(0f, maxReserve));
                            update = true;
                        }
                        else
                        {
                            if (ReserveData[charBody.netId].maxReserve != maxReserve)
                            {
                                ReserveData[charBody.netId].maxReserve = maxReserve;
                                update = true;
                            }
                        }

                        // Force Reserve Value Update
                        if (update) healthComponent.Heal(0.01f, default, true);
                    }
                }
            }
        }

        private void OnDestroyHook()
        {
            On.RoR2.CharacterBody.OnDestroy += (self, orig) =>
            {
                if (NetworkServer.active)
                {
                    if (ReserveData.ContainsKey(orig.netId))
                    {
                        ReserveData.Remove(orig.netId);
                    }
                }

                self(orig);
            };
        }



        private void FixedUpdateValueHook()
        {
            IL.RoR2.HealthComponent.RepeatHealComponent.FixedUpdate += (il) =>
            {
                var c = new ILCursor(il);

                c.GotoNext(
                    x => x.MatchLdarg(0),
                    x => x.MatchLdarg(0),
                    x => x.MatchLdfld("RoR2.HealthComponent/RepeatHealComponent", "reserve"),
                    x => x.MatchLdloc(0),
                    x => x.MatchSub(),
                    x => x.MatchStfld("RoR2.HealthComponent/RepeatHealComponent", "reserve")
                );

                c.Index += 6;

                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("healthComponent"));
                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("reserve"));
                c.EmitDelegate<Action<HealthComponent, float>>((healthComponent, reserve) =>
                {
                    if (healthComponent.body != null)
                    {
                        if (ReserveData.ContainsKey(healthComponent.body.netId))
                        {
                            ReserveData[healthComponent.body.netId].currentReserve = reserve;
                        }
                    }
                });
            };
        }

        private void AddReserveValueHook()
        {
            IL.RoR2.HealthComponent.RepeatHealComponent.AddReserve += (il) =>
            {
                var c = new ILCursor(il);

                c.GotoNext(
                    x => x.MatchRet()
                );

                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("healthComponent"));
                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("reserve"));
                c.Emit(OpCodes.Ldarg, 2);
                c.EmitDelegate<Action<HealthComponent, float, float>>((healthComponent, reserve, max) =>
                {
                    if (healthComponent.body != null)
                    {
                        if (!ReserveData.ContainsKey(healthComponent.body.netId))
                        {
                            ReserveData.Add(healthComponent.body.netId, new ReserveAmountMessage(reserve, max));
                        }
                        else
                        {
                            ReserveData[healthComponent.body.netId].currentReserve = reserve;
                            ReserveData[healthComponent.body.netId].maxReserve = max;
                        }
                    }
                });
            };
        }



        private void AllowRepeatHealHook()
        {
            IL.RoR2.HealthComponent.RepeatHealComponent.FixedUpdate += (il) =>
            {
                ILCursor c = new ILCursor(il);

                c.GotoNext(
                    x => x.MatchSub(),
                    x => x.MatchStfld("RoR2.HealthComponent/RepeatHealComponent", "timer")
                );

                c.Index += 1;

                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("healthComponent"));
                c.Emit(OpCodes.Ldarg_0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("reserve"));
                c.EmitDelegate<Func<float, HealthComponent, float, float>>((newTime, healthComponent, reserve) =>
                {
                    if (reserve == 0f) return 0.2f;
                    if (GetMinReserveRate(healthComponent) == 0f && healthComponent.health == healthComponent.fullHealth) return 0.2f;
                    if (healthComponent.body.HasBuff(BuffIndex.HealingDisabled)) return 0.2f;
                    return newTime;
                });
            };
        }

        private void HealHook()
        {
            IL.RoR2.HealthComponent.Heal += (il) =>
            {
                ILCursor c = new ILCursor(il);

                // Set Heal Rate
                c.GotoNext(
                    x => x.MatchLdfld("RoR2.HealthComponent/ItemCounts", "repeatHeal"),
                    x => x.MatchConvR4(),
                    x => x.MatchDiv()
                );

                c.Index += 3;

                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Ldarg, 0);
                c.EmitDelegate<Func<HealthComponent, float>>((healthComponent) =>
                {
                    return GetMaxReserveRate(healthComponent);
                });

                // Move To Target
                c.GotoNext(
                    x => x.MatchAdd(),
                    x => x.MatchConvR4(),
                    x => x.MatchMul(),
                    x => x.MatchLdarg(0)
                );

                c.Index += 5;

                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Pop);
                // Set Amount
                c.Emit(OpCodes.Ldarg, 1);
                // Set Max Reserve
                c.Emit(OpCodes.Ldarg, 0);
                c.EmitDelegate<Func<HealthComponent, float>>((healthComponent) =>
                {
                    float maxReserve = healthComponent.fullHealth * Mathf.Max(0.1f, BaseHealthReserve.Value);

                    if (healthComponent.body != null)
                    {
                        if (!ReserveData.ContainsKey(healthComponent.body.netId))
                        {
                            CalculateReserveCapacity(healthComponent);
                        }

                        if (ReserveData.ContainsKey(healthComponent.body.netId))
                        {
                            maxReserve = ReserveData[healthComponent.body.netId].maxReserve;
                        }
                    }

                    return maxReserve;
                });
            };
        }



        private void FixedUpdateAmountHook()
        {
            IL.RoR2.HealthComponent.RepeatHealComponent.FixedUpdate += (il) =>
            {
                var c = new ILCursor(il);

                c.GotoNext(
                    x => x.MatchLdfld("RoR2.HealthComponent/RepeatHealComponent", "reserve"),
                    x => x.MatchCallOrCallvirt<Mathf>("Min"),
                    x => x.MatchStloc(0)
                );

                c.Index += 2;

                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("healthComponent"));
                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("reserve"));
                c.EmitDelegate<Func<HealthComponent, float, float>>((healthComponent, reserve) =>
                {
                    int count = HealthComponentItemCount(healthComponent, ItemIndex.RepeatHeal);
                    float factor = GetHealScaleFactor(healthComponent);
                    float mult = Mathf.Max(0.1f, 1f + BaseHealMult.Value + ((count - 1) * StackHealMult.Value));
                    // highest between - min heal amount - heal missing health
                    float toHealth = Mathf.Max(healthComponent.fullHealth * GetMinReserveRate(healthComponent) * 0.2f, (healthComponent.fullHealth - healthComponent.health) / (factor*mult));
                    // lowest between : reserve - max heal amount - toHealth
                    return Mathf.Min(reserve, healthComponent.fullHealth * GetMaxReserveRate(healthComponent) * 0.2f, toHealth) * mult;
                });
            };
        }

        private void AddReserveAmountHook()
        {
            IL.RoR2.HealthComponent.RepeatHealComponent.AddReserve += (il) =>
            {
                var c = new ILCursor(il);

                c.GotoNext(
                    x => x.MatchLdarg(2),
                    x => x.MatchCallOrCallvirt<Mathf>("Min"),
                    x => x.MatchStfld("RoR2.HealthComponent/RepeatHealComponent", "reserve")
                );

                c.Index += 2;

                c.Emit(OpCodes.Pop);
                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("healthComponent"));
                c.Emit(OpCodes.Ldarg, 0);
                c.Emit(OpCodes.Ldfld, typeof(HealthComponent).GetNestedType("RepeatHealComponent", BindingFlags.Instance | BindingFlags.NonPublic).GetFieldCached("reserve"));
                c.Emit(OpCodes.Ldarg, 1);
                c.Emit(OpCodes.Ldarg, 2);
                c.EmitDelegate<Func<HealthComponent, float, float, float, float>>((healthComponent, reserve, amount, max) =>
                {
                    int count = HealthComponentItemCount(healthComponent, ItemIndex.RepeatHeal);
                    float factor = GetHealScaleFactor(healthComponent);
                    float mult = Mathf.Max(0.1f, 1f + BaseHealMult.Value + ((count - 1) * StackHealMult.Value));
                    float absorb = Mathf.Max(0.1f, 1f + BaseAbsorbMult.Value + ((count - 1) * StackAbsorbMult.Value));

                    // prevent heal mult double dipping 
                    amount /= factor;

                    float toHealth = 0f;
                    float toReserve = 0f;

                    if (HealBeforeReserve.Value)
                    {
                        // - lowest between : amount - heal missing health
                        toHealth = Math.Min(amount, (healthComponent.fullHealth - healthComponent.health) / (factor*mult));
                        amount -= toHealth;
                    }

                    if (amount > 0f)
                    {
                        // - lowest between : amount - fill missing reserve
                        toReserve = Math.Min(amount, (max - reserve) / absorb);
                        amount -= toReserve;
                    }

                    if (amount > 0f && HealReserveFull.Value)
                    {
                        toHealth += amount;
                        amount = 0f;
                    }



                    if (toHealth > 0f)
                    {
                        ProcChainMask procChainMask = default;
                        procChainMask.AddProc(ProcType.RepeatHeal);
                        healthComponent.Heal(toHealth * mult, procChainMask, true);
                    }

                    return Mathf.Min(max, reserve + (toReserve * absorb));
                });
            };
        }



        private void TextSetup()
        {
            string output = "";
            string temp;

            if (HealBeforeReserve.Value) output += "<style=cIsUtility>Gain extra healing as reserve</style>.\n";
            else if (HealReserveFull.Value) output += "<style=cIsUtility>Store Healing to heal over time</style>.\n";
            else output += "<style=cIsUtility>All Healing is applied over time</style>.\n";

            // reserve absorption
            if (BaseAbsorbMult.Value != 0f || StackAbsorbMult.Value != 0f)
            {
                if (StackAbsorbMult.Value == 0f)
                {
                    if (BaseAbsorbMult.Value > 0f) temp = "<style=cIsHealing>";
                    else temp = "<style=cDeath>";
                }
                else if (StackAbsorbMult.Value > 0f)
                {
                    if (BaseAbsorbMult.Value >= 0f) temp = "<style=cIsHealing>";
                    else temp = "<style=cIsDamage>";
                }
                else
                {
                    if (BaseAbsorbMult.Value > 0f) temp = "<style=cIsDamage>";
                    else temp = "<style=cDeath>";
                }

                output += "\n" + temp;
                if (BaseAbsorbMult.Value >= 0f) output += "+";
                output += $"{BaseAbsorbMult.Value * 100f:0.##}%</style>";

                if (StackAbsorbMult.Value != 0f)
                {
                    output += " <style=cStack>(";
                    if (StackAbsorbMult.Value > 0f) output += "+";
                    output += $"{StackAbsorbMult.Value * 100f:0.##}% per stack)</style>";
                }

                output += " " + temp + "Absorption Multiplier</style>.";
            }

            // heal multiplier
            if (BaseHealMult.Value != 0f || StackHealMult.Value != 0f)
            {
                if (StackHealMult.Value == 0f)
                {
                    if (BaseHealMult.Value > 0f) temp = "<style=cIsHealing>";
                    else temp = "<style=cDeath>";
                }
                else if (StackHealMult.Value > 0f)
                {
                    if (BaseHealMult.Value >= 0f) temp = "<style=cIsHealing>";
                    else temp = "<style=cIsDamage>";
                }
                else
                {
                    if (BaseHealMult.Value > 0f) temp = "<style=cIsDamage>";
                    else temp = "<style=cDeath>";
                }

                output += "\n" + temp;
                if (BaseHealMult.Value >= 0f) output += "+";
                output += $"{BaseHealMult.Value * 100f:0.##}%</style>";

                if (StackHealMult.Value != 0f)
                {
                    output += " <style=cStack>(";
                    if (StackHealMult.Value > 0f) output += "+";
                    output += $"{StackHealMult.Value * 100f:0.##}% per stack)</style>";
                }

                output += " " + temp + "Healing Multiplier</style>.";
            }

            // reserve amount
            output += "\n<style=cIsHealing>Gain ";
            output += $"{BaseHealthReserve.Value * 100f:0.##}%</style>";

            if (StackHealthReserve.Value != 0f)
            {
                output += " <style=cStack>(";
                if (StackHealthReserve.Value > 0f) output += "+";
                output += $"{StackHealthReserve.Value * 100f:0.##}% per stack)</style>";
            }

            output += " <style=cIsHealing>maximum health</style> as <style=cIsHealing>reserve</style>.";

            // recovery rate
            output += "\n<style=cIsHealing>Heal</style> up to <style=cIsHealing>";
            output += $"{BaseMaxUsageRate.Value * 100f:0.##}%</style>";

            if (StackMaxUsageRate.Value != 0f)
            {
                output += " <style=cStack>(";
                if (StackMaxUsageRate.Value > 0f) output += "+";
                output += $"{StackMaxUsageRate.Value * 100f:0.##}% per stack)</style>";
            }

            output += " of your <style=cIsHealing>maximum health per second</style> from <style=cIsHealing>reserve</style>.";

            // decay rate
            if (BaseMinUsageRate.Value != 0f || StackMinUsageRate.Value != 0f)
            {
                output += "\n<style=cIsHealing>Reserve</style> <style=cIsDamage>decays</style> at <style=cIsDamage>";
                output += $"{BaseMinUsageRate.Value * 100f:0.##}%</style>";

                if (StackMinUsageRate.Value != 0f)
                {
                    output += " <style=cStack>(";
                    if (StackMinUsageRate.Value > 0f) output += "+";
                    output += $"{StackMinUsageRate.Value * 100f:0.##}% per stack)</style>";
                }

                output += " of your <style=cIsDamage>maximum health per second</style>.";
            }



            if (HealBeforeReserve.Value) LanguageAPI.Add("ITEM_REPEATHEAL_PICKUP", "Gain extra healing as reserve.");
            else if (HealReserveFull.Value) LanguageAPI.Add("ITEM_REPEATHEAL_PICKUP", "Store Healing to heal over time.");
            else LanguageAPI.Add("ITEM_REPEATHEAL_PICKUP", "All Healing is applied over time.");

            LanguageAPI.Add("ITEM_REPEATHEAL_DESC", output);
        }



        public void Awake()
        {
            ConfigSetup();

            HealthUIAwakeHook();
            HealthUIUpdateHook();

            RecalculateStatsHook();
            OnDestroyHook();

            FixedUpdateValueHook();
            AddReserveValueHook();

            AllowRepeatHealHook();
            HealHook();

            FixedUpdateAmountHook();
            AddReserveAmountHook();

            TextSetup();
        }

        public void Update()
        {
            //Debugger();

            UpdateClientReserves();
            UpdateServerReserves();
        }


        /*
        private static void Debugger()
        {
            bool debugDropper = true;

            if (debugDropper)
            {
                if (Input.GetKeyDown(KeyCode.F2))
                {
                    var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.RepeatHeal), transform.position, transform.forward * 20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.ShieldOnly), transform.position, transform.forward * -20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.LunarDagger), transform.position, transform.right * 20f);
                }
                if (Input.GetKeyDown(KeyCode.F3))
                {
                    var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.Knurl), transform.position, transform.forward * 20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.IncreaseHealing), transform.position, transform.forward * -20f);
                }
                if (Input.GetKeyDown(KeyCode.F4))
                {
                    var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.AttackSpeedOnCrit), transform.position, transform.forward * 20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.HealOnCrit), transform.position, transform.forward * 20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.CritGlasses), transform.position, transform.forward * 20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.CritGlasses), transform.position, transform.forward * 20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.CritGlasses), transform.position, transform.forward * 20f);
                }
                if (Input.GetKeyDown(KeyCode.F5))
                {
                    var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.BarrierOnKill), transform.position, transform.forward * 20f);
                    PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(ItemIndex.PersonalShield), transform.position, transform.forward * 20f);
                }
            }
        }
        */


        private void UpdateClientReserves()
        {
            if (NetworkServer.active)
            {
                foreach (NetworkUser user in NetworkUser.readOnlyInstancesList)
                {
                    if (ReserveTarget.ContainsKey(user.netId))
                    {
                        var targetBodyNetId = ReserveTarget[user.netId].netId;

                        // update client display values
                        if (ReserveData.ContainsKey(targetBodyNetId))
                        {
                            UpdateReserveAmountCommand.Invoke(ReserveData[targetBodyNetId], user);
                        }
                        else
                        {
                            UpdateReserveAmountCommand.Invoke(defaultAmountMessage, user);
                        }
                    }
                }
            }
        }

        private void UpdateServerReserves()
        {
            if (NetworkServer.active)
            {
                // update server display values
                if (ReserveData.ContainsKey(netId))
                {
                    currentReserve = ReserveData[netId].currentReserve;
                    maxReserve = ReserveData[netId].maxReserve;
                }
                else
                {
                    currentReserve = 0f;
                    maxReserve = -1f;
                }
            }
        }
    }
}