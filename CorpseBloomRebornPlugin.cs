using UnityEngine;
using BepInEx;
using RoR2;
using RoR2.ContentManagement;
using System.Linq;
using System.Collections.Generic;

using System.Security;
using System.Security.Permissions;

[module: UnverifiableCode]
#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

namespace TPDespair.CorpseBloomReborn
{
	[BepInPlugin(ModGuid, ModName, ModVer)]

	public class CorpseBloomRebornPlugin : BaseUnityPlugin
	{
		public const string ModVer = "1.2.0";
		public const string ModName = "CorpseBloomReborn";
		public const string ModGuid = "com.TPDespair.CorpseBloomReborn";

		public static Dictionary<string, string> LangTokens = new Dictionary<string, string>();

		public static ArtifactIndex DiluvianArtifact = ArtifactIndex.None;



		public void Awake()
		{
			RoR2Application.isModded = true;
			NetworkModCompatibilityHelper.networkModList = NetworkModCompatibilityHelper.networkModList.Append(ModGuid + ":" + ModVer);

			Configuration.Init(Config);

			ContentManager.collectContentPackProviders += ContentManager_collectContentPackProviders;

			ReserveManager.Init();
			RoR2Application.onLoad += ApplicationOnLoad;

			HudAwakeHook();
			AllyCardAwakeHook();

			RoR2.UI.HUD.onHudTargetChangedGlobal += HudTargetChanged;

			LanguageOverride();
			RegisterLanguageToken("ITEM_REPEATHEAL_DESC", GetCorpseBloomDesc());
			RegisterLanguageToken("ITEM_REPEATHEAL_PICKUP", GetCorpseBloomPickup());

			//On.RoR2.Networking.NetworkManagerSystemSteam.OnClientConnect += (s, u, t) => { };
		}

		

		public void FixedUpdate()
		{
			ReserveManager.OnFixedUpdate();
		}
		/*
		public void Update()
		{
			DebugDrops();
		}
		//*/



		private void ContentManager_collectContentPackProviders(ContentManager.AddContentPackProviderDelegate addContentPackProvider)
		{
			addContentPackProvider(new CorpseBloomRebornContent());
		}



		private static void ApplicationOnLoad()
		{
            Run.onRunStartGlobal += RunStartHealPriority;

			ArtifactIndex index = ArtifactCatalog.FindArtifactIndex("ARTIFACT_DILUVIFACT");
			if (index != ArtifactIndex.None)
			{
				DiluvianArtifact = index;
				RunArtifactManager.onArtifactEnabledGlobal += ArtifactEnabledHealPriority;
			}
		}

        private static void RunStartHealPriority(Run run)
        {
			ReserveManager.RequestPriorityHealHook();
		}

		private static void ArtifactEnabledHealPriority(RunArtifactManager manager, ArtifactDef artifactDef)
		{
			if (artifactDef)
			{
				if (artifactDef.artifactIndex == DiluvianArtifact)
				{
					ReserveManager.RequestPriorityHealHook();
				}
			}
		}



		private static void HudAwakeHook()
		{
			On.RoR2.UI.HUD.Awake += (orig, self) =>
			{
				orig(self);

				HudReserveDisplay display = self.gameObject.AddComponent<HudReserveDisplay>();
				display.hud = self;
			};
		}

		private static void AllyCardAwakeHook()
		{
			On.RoR2.UI.AllyCardController.Awake += (orig, self) =>
			{
				orig(self);

				AllyReserveDisplay display = self.gameObject.AddComponent<AllyReserveDisplay>();
				display.controller = self;
			};
		}

		private static void HudTargetChanged(RoR2.UI.HUD hud)
		{
			HudReserveDisplay display = hud.GetComponent<HudReserveDisplay>();
			if (display)
			{
				display.RequestRebuild();
			}
		}



		private static void LanguageOverride()
		{
			On.RoR2.Language.TokenIsRegistered += (orig, self, token) =>
			{
				if (token != null)
				{
					if (LangTokens.ContainsKey(token)) return true;
				}

				return orig(self, token);
			};

			On.RoR2.Language.GetString_string += (orig, token) =>
			{
				if (token != null)
				{
					if (LangTokens.ContainsKey(token)) return LangTokens[token];
				}

				return orig(token);
			};
		}

		public static void RegisterLanguageToken(string token, string text)
		{
			if (!LangTokens.ContainsKey(token)) LangTokens.Add(token, text);
			else LangTokens[token] = text;
		}

		private static string GetCorpseBloomDesc()
		{
			string output = "";
			string temp;

			if (Configuration.HealBeforeReserve.Value) output += "Gain extra healing as reserve.";
			else if (Configuration.HealWhenReserveFull.Value) output += "Store healing to heal over time.";
			else output += "All healing is applied over time.";

			output += "\n";

			output += "\nGain <style=cIsHealing>";
			output += $"{Configuration.BaseHealthReserve.Value * 100f:0.##}%</style>";
			if (Configuration.AddedHealthReserve.Value != 0f)
			{
				output += " <style=cStack>(";
				if (Configuration.AddedHealthReserve.Value > 0f) output += "+";
				output += $"{Configuration.AddedHealthReserve.Value * 100f:0.##}% per stack)</style>";
			}
			output += " of your <style=cIsHealing>maximum health</style> as <style=cIsHealing>maximum reserve</style>.";

			output += "\nStore <style=cIsHealing>" + Configuration.BaseAbsorbMult.Value * 100f + "%</style>";
			if (Configuration.AddedAbsorbMult.Value != 0f)
			{
				output += " <style=cStack>(";
				if (Configuration.AddedAbsorbMult.Value > 0f) output += "+";
				output += Configuration.AddedAbsorbMult.Value * 100f + "% per stack)</style>";
			}
			output += " of healing as <style=cIsHealing>reserve</style>.";

			output += "\nCan <style=cIsHealing>heal</style> for <style=cIsHealing>";
			output += $"{Configuration.BaseMaxUsageRate.Value * 100f:0.##}%</style>";
			if (Configuration.StackMaxUsageRate.Value != 0f)
			{
				output += " <style=cStack>(";
				if (Configuration.StackMaxUsageRate.Value > 0f) output += "+";
				output += $"{Configuration.StackMaxUsageRate.Value * 100f:0.##}% per stack)</style>";
			}
			output += " of your <style=cIsHealing>maximum health</style> every second from <style=cIsHealing>reserve</style>.";
			if (Configuration.VanillaUsageBehavior.Value)
			{
				output += "\n<style=cStack>(Additional stacks further reduce healing rate)</style>";
			}

			output += "\n";

			if (Configuration.BaseExportMult.Value != 1f || Configuration.AddedExportMult.Value != 0f)
			{
				if (Configuration.AddedExportMult.Value == 0f)
				{
					if (Configuration.BaseExportMult.Value > 1f) temp = "<style=cIsHealing>";
					else temp = "<style=cDeath>";
				}
				else if (Configuration.AddedExportMult.Value > 0f)
				{
					if (Configuration.BaseExportMult.Value >= 1f) temp = "<style=cIsHealing>";
					else temp = "<style=cIsDamage>";
				}
				else
				{
					if (Configuration.BaseExportMult.Value > 1f) temp = "<style=cIsDamage>";
					else temp = "<style=cDeath>";
				}

				output += "\n" + temp;
				if (Configuration.BaseExportMult.Value >= 1f) output += "+";
				output += $"{(Configuration.BaseExportMult.Value - 1f) * 100f:0.##}%</style>";
				if (Configuration.AddedExportMult.Value != 0f)
				{
					output += " <style=cStack>(";
					if (Configuration.AddedExportMult.Value > 0f) output += "+";
					output += $"{Configuration.AddedExportMult.Value * 100f:0.##}% per stack)</style>";
				}
				output += " Healing Multiplier.";
			}

			return output;
		}

		private static string GetCorpseBloomPickup()
		{
			if (Configuration.HealBeforeReserve.Value) return "Gain extra healing as reserve.";
			else if (Configuration.HealWhenReserveFull.Value) return "Store healing to heal over time.";
			else return "All healing is applied over time.";
		}


		/*
		private static void DebugDrops()
		{
			if (Input.GetKeyDown(KeyCode.F2))
			{
				var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;

				CreateDroplet(RoR2Content.Items.RepeatHeal, transform.position + new Vector3(-5f, 5f, 5f));
				CreateDroplet(EquipmentCatalog.GetEquipmentDef(EquipmentCatalog.FindEquipmentIndex("EliteEarthEquipment")), transform.position + new Vector3(0f, 5f, 7.5f));
				CreateDroplet(RoR2Content.Items.Mushroom, transform.position + new Vector3(5f, 5f, 5f));
				CreateDroplet(RoR2Content.Items.Mushroom, transform.position + new Vector3(5f, 5f, 5f));
				CreateDroplet(RoR2Content.Items.IncreaseHealing, transform.position + new Vector3(-5f, 5f, -5f));
				CreateDroplet(RoR2Content.Items.Knurl, transform.position + new Vector3(0f, 5f, -7.5f));
				CreateDroplet(RoR2Content.Items.PersonalShield, transform.position + new Vector3(5f, 5f, -5f));
			}
			if (Input.GetKeyDown(KeyCode.F3))
			{
				var transform = PlayerCharacterMasterController.instances[0].master.GetBodyObject().transform;

				CreateDroplet(DLC1Content.Items.MushroomVoid, transform.position + new Vector3(-5f, 5f, 5f));
				CreateDroplet(DLC1Content.Items.MissileVoid, transform.position + new Vector3(0f, 5f, 7.5f));
				CreateDroplet(RoR2Content.Equipment.Fruit, transform.position + new Vector3(5f, 5f, 5f));
				CreateDroplet(RoR2Content.Items.EquipmentMagazine, transform.position + new Vector3(-5f, 5f, -5f));
				CreateDroplet(RoR2Content.Items.EquipmentMagazine, transform.position + new Vector3(-5f, 5f, -5f));
				CreateDroplet(DLC1Content.Items.HealingPotion, transform.position + new Vector3(0f, 5f, -7.5f));
				CreateDroplet(DLC1Content.Items.FragileDamageBonus, transform.position + new Vector3(5f, 5f, -5f));
			}
			if (Input.GetKeyDown(KeyCode.F4))
			{
				RegisterLanguageToken("ITEM_REPEATHEAL_DESC", GetCorpseBloomDesc());
				RegisterLanguageToken("ITEM_REPEATHEAL_PICKUP", GetCorpseBloomPickup());
			}
		}

		private static void CreateDroplet(EquipmentDef def, Vector3 pos)
		{
			if (!def) return;

			PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(def.equipmentIndex), pos, Vector3.zero);
		}

		private static void CreateDroplet(ItemDef def, Vector3 pos)
		{
			if (!def) return;

			PickupDropletController.CreatePickupDroplet(PickupCatalog.FindPickupIndex(def.itemIndex), pos, Vector3.zero);
		}
		//*/
	}
}
