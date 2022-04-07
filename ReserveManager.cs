using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using RoR2;

namespace TPDespair.CorpseBloomReborn
{
	public static class ReserveManager
	{
		public static Dictionary<NetworkInstanceId, ReserveInfo> ReserveData = new Dictionary<NetworkInstanceId, ReserveInfo>();
		private static Dictionary<NetworkInstanceId, float> DestroyedBodies = new Dictionary<NetworkInstanceId, float>();

		private static float DestroyFixedUpdateStopwatch = 0f;
		private const float ReserveUpdateInterval = 0.25f;

		internal static BuffDef IndicatorBuff;

		private static bool Rehook = false;
		private static float RehookTimer = 0f;
		private static bool HealHooked = false;

		public class ReserveInfo
		{
			public HealthComponent healthComponent;

			public float healTimer = ReserveUpdateInterval;
			public float displayTimer = ReserveUpdateInterval;

			public int buffCount = 0;
			public float maximum = 1f;
			public float reserve = 0f;

			public float maxRate = 0.1f;
			public float minRate = 0f;

			public float absorbMult = 1f;
			public float exportMult = 1f;

			public bool healingDisabled = false;
		}



		public static ReserveInfo GetReserveInfo(HealthComponent healthComponent)
		{
			CharacterBody body = healthComponent.body;
			if (body)
			{
				return GetReserveInfo(body);
			}

			return null;
		}

		public static ReserveInfo GetReserveInfo(CharacterBody body)
		{
			if (DestroyedBodies.ContainsKey(body.netId)) return null;

			ReserveInfo reserveInfo;

			if (!ReserveData.ContainsKey(body.netId))
			{
				reserveInfo = new ReserveInfo();
				ReserveData.Add(body.netId, reserveInfo);

				RecalcReserveInfo(body);
			}

			reserveInfo = ReserveData[body.netId];

			return reserveInfo;
		}

		private static void RecalcReserveInfo(CharacterBody body)
		{
			ReserveInfo reserveInfo = GetReserveInfo(body);
			if (reserveInfo != null)
			{
				float reserveMult = 1f;

				float maxRate = 0.1f;
				float minRate = 0f;

				float absorbMult = 1f;
				float exportMult = 1f;

				if (!reserveInfo.healthComponent)
				{
					HealthComponent healthComponent = body.healthComponent;
					if (healthComponent)
					{
						reserveInfo.healthComponent = healthComponent;
					}
				}

				Inventory inventory = body.inventory;
				if (inventory)
				{
					int count = inventory.GetItemCount(RoR2Content.Items.RepeatHeal);
					if (count > 0)
					{
						reserveMult = Mathf.Max(0.1f, Configuration.BaseHealthReserve.Value + Configuration.AddedHealthReserve.Value * (count - 1));

						minRate = Mathf.Max(0f, Configuration.BaseMinUsageRate.Value + (Configuration.StackMinUsageRate.Value * (count - 1)));
						maxRate = Mathf.Max(0.01f, minRate, Configuration.BaseMaxUsageRate.Value + (Configuration.StackMaxUsageRate.Value * (count - 1)));

						if (Configuration.VanillaUsageBehavior.Value)
						{
							minRate = Mathf.Max(0f, minRate / count);
							maxRate = Mathf.Max(0.01f, maxRate / count);
						}

						absorbMult = Mathf.Max(0.1f, Configuration.BaseAbsorbMult.Value + (Configuration.AddedAbsorbMult.Value * (count - 1)));
						exportMult = Mathf.Max(0.1f, Configuration.BaseExportMult.Value + (Configuration.AddedExportMult.Value * (count - 1)));
					}
					else
					{
						reserveInfo.reserve = 0f;
					}

					if (Configuration.RestoreRejuvBehavior.Value)
					{
						count = inventory.GetItemCount(RoR2Content.Items.IncreaseHealing);
						if (count > 0)
						{
							exportMult *= 1f + count;
						}
					}
				}

				reserveInfo.maximum = Mathf.Max(1f, body.maxHealth * reserveMult);
				reserveInfo.reserve = Mathf.Min(reserveInfo.reserve, reserveInfo.maximum);

				reserveInfo.maxRate = maxRate;
				reserveInfo.minRate = minRate;

				reserveInfo.absorbMult = absorbMult;
				reserveInfo.exportMult = exportMult;

				reserveInfo.healingDisabled = body.HasBuff(RoR2Content.Buffs.HealingDisabled);
			}
		}

		public static void AddReserve(ReserveInfo reserveInfo, float amount)
		{
			float toHealth = 0f;
			float toReserve = 0f;

			HealthComponent healthComponent = reserveInfo.healthComponent;
			if (healthComponent)
			{
				if (Configuration.HealBeforeReserve.Value)
				{
					// - lowest between : amount - heal missing health
					toHealth = Math.Min(amount, (healthComponent.fullHealth - healthComponent.health) / reserveInfo.exportMult);
					amount -= toHealth;
				}

				if (amount > 0f)
				{
					// - lowest between : amount - fill missing reserve
					toReserve = Math.Min(amount, (reserveInfo.maximum - reserveInfo.reserve) / reserveInfo.absorbMult);
					amount -= toReserve;
				}

				if (amount > 0f && Configuration.HealWhenReserveFull.Value)
				{
					toHealth += amount;
				}

				if (toHealth > 0f)
				{
					ProcChainMask procChainMask = default;
					procChainMask.AddProc(ProcType.RepeatHeal);
					healthComponent.Heal(toHealth * reserveInfo.exportMult, procChainMask, true);
				}
			}
			else
			{
				toReserve = amount;
			}

			reserveInfo.reserve = Mathf.Min(reserveInfo.reserve + (toReserve * reserveInfo.absorbMult), reserveInfo.maximum);
			/*
			Debug.LogWarning("-----");
			Debug.LogWarning("CBR AddReserve - toHealth : " + toHealth + " (" + (toHealth * reserveInfo.exportMult) + ") [" + (amount * reserveInfo.exportMult) + "]   toReserve : " + toReserve + " (" + (toReserve * reserveInfo.absorbMult) + ")");
			Debug.LogWarning($"{reserveInfo.reserve:0.#}" + "/" + $"{reserveInfo.maximum:0.#}" + " : " + $"{(reserveInfo.reserve / reserveInfo.maximum * 100f):0.#}%");
			*/
		}



		internal static void Init()
		{
			RecalculateReserveHook();
			DestroyReserveHook();
		}

		internal static void OnFixedUpdate()
		{
			PrioritizeHealHook();
			DestroyReserveData();
			if (NetworkServer.active)
			{
				if (Run.instance) UpdateReserveInfos();
			}
		}



		private static void RecalculateReserveHook()
		{
			On.RoR2.CharacterBody.RecalculateStats += (orig, self) =>
			{
				orig(self);

				if (NetworkServer.active && self)
				{
					if (ReserveData.ContainsKey(self.netId))
					{
						RecalcReserveInfo(self);
					}
				}
			};
		}

		private static void DestroyReserveHook()
		{
			On.RoR2.CharacterBody.OnDestroy += (orig, self) =>
			{
				if (NetworkServer.active & self)
				{
					DestroyedBodies.Add(self.netId, 3.5f);

					if (ReserveData.ContainsKey(self.netId))
					{
						ReserveData[self.netId].healthComponent = null;
					}
				}

				orig(self);
			};
		}



		internal static void RequestPriorityHealHook()
		{
			RehookTimer = 1f;
			Rehook = true;
		}

		private static void PrioritizeHealHook()
		{
			if (Rehook)
			{
				RehookTimer -= Time.fixedDeltaTime;
				if (RehookTimer <= 0f)
				{
					RehookTimer = 1f;
					Rehook = false;

					if (HealHooked)
					{
						On.RoR2.HealthComponent.Heal -= PriorityHealHook;
						HealHooked = false;
					}

					On.RoR2.HealthComponent.Heal += PriorityHealHook;
					HealHooked = true;
				}
			}
		}

		private static float PriorityHealHook(On.RoR2.HealthComponent.orig_Heal orig, HealthComponent self, float amount, ProcChainMask procChainMask, bool nonRegen)
		{
			if (NetworkServer.active)
			{
				if (nonRegen && self.repeatHealComponent && !procChainMask.HasProc(ProcType.RepeatHeal))
				{
					if (self.alive && amount > 0f && !self.body.HasBuff(RoR2Content.Buffs.HealingDisabled))
					{
						ReserveInfo reserveInfo = GetReserveInfo(self);
						if (reserveInfo != null)
						{
							AddReserve(reserveInfo, amount);
						}

						return 0f;
					}
					else
					{
						return 0f;
					}
				}
			}

			return orig(self, amount, procChainMask, nonRegen);
		}



		private static void DestroyReserveData()
		{
			DestroyFixedUpdateStopwatch += Time.fixedDeltaTime;

			if (DestroyFixedUpdateStopwatch >= 0.5f)
			{
				List<NetworkInstanceId> destroyedBodiesKeys = new List<NetworkInstanceId>(DestroyedBodies.Keys);

				foreach (var netId in destroyedBodiesKeys)
				{
					DestroyedBodies[netId] -= DestroyFixedUpdateStopwatch;
					if (DestroyedBodies[netId] <= 0f)
					{
						DestroyedBodies.Remove(netId);

						if (ReserveData.ContainsKey(netId))
						{
							ReserveData.Remove(netId);
						}
					}
				}

				DestroyFixedUpdateStopwatch = 0f;
			}
		}

		private static void UpdateReserveInfos()
		{
			float deltaTime = Time.fixedDeltaTime;

			List<NetworkInstanceId> reserveDataKeys = new List<NetworkInstanceId>(ReserveData.Keys);

			foreach (var netId in reserveDataKeys)
			{
				if (!DestroyedBodies.ContainsKey(netId))
				{
					UpdateReserveInfo(ReserveData[netId], deltaTime);
				}
			}
		}

		private static void UpdateReserveInfo(ReserveInfo reserveInfo, float deltaTime)
		{
			HealthComponent healthComponent = reserveInfo.healthComponent;
			if (healthComponent)
			{
				if (!healthComponent.alive) return;

				reserveInfo.displayTimer -= deltaTime;

				// Display
				if (reserveInfo.displayTimer <= 0f)
				{
					reserveInfo.displayTimer = ReserveUpdateInterval;

					CharacterBody body = healthComponent.body;
					if (body)
					{
						int buffCount = 0;

						if (reserveInfo.reserve > 0f)
						{
							buffCount = Mathf.Clamp(Mathf.RoundToInt(reserveInfo.reserve / reserveInfo.maximum * 100f), 1, 100);
						}

						reserveInfo.buffCount = buffCount;

						if (body.GetBuffCount(IndicatorBuff) != buffCount)
						{
							body.SetBuffCount(IndicatorBuff.buffIndex, buffCount);
						}
					}
				}

				// Healing
				if (ResetTimer(reserveInfo) || (reserveInfo.minRate == 0f && healthComponent.health >= healthComponent.fullHealth))
				{
					reserveInfo.healTimer = ReserveUpdateInterval;
				}
				else
				{
					reserveInfo.healTimer -= deltaTime;

					if (reserveInfo.healTimer <= 0f)
					{
						reserveInfo.healTimer = ReserveUpdateInterval;

						reserveInfo.displayTimer = Mathf.Clamp(reserveInfo.displayTimer, 0.15f, 0.2f);

						float amount = ReserveHealAmount(reserveInfo);
						reserveInfo.reserve -= amount;

						ProcChainMask procChainMask = default(ProcChainMask);
						procChainMask.AddProc(ProcType.RepeatHeal);
						reserveInfo.healthComponent.Heal(amount * reserveInfo.exportMult, procChainMask, true);
					}
				}
			}
		}

		private static bool ResetTimer(ReserveInfo reserveInfo)
		{
			if (reserveInfo.reserve == 0f) return true;
			if (reserveInfo.healingDisabled) return true;

			return false;
		}

		private static float ReserveHealAmount(ReserveInfo reserveInfo)
		{
			HealthComponent healthComponent = reserveInfo.healthComponent;

			// highest between - min heal amount - heal missing health
			float healMissingHealth = (healthComponent.fullHealth - healthComponent.health) / reserveInfo.exportMult;
			float toHealth = Mathf.Max(healthComponent.fullHealth * reserveInfo.minRate * ReserveUpdateInterval, healMissingHealth);
			// lowest between : reserve - max heal amount - toHealth
			return Mathf.Min(reserveInfo.reserve, healthComponent.fullHealth * reserveInfo.maxRate * ReserveUpdateInterval, toHealth);
		}
	}
}
