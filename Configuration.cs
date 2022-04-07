using BepInEx.Configuration;

namespace TPDespair.CorpseBloomReborn
{
	public static class Configuration
	{
		public static ConfigEntry<bool> HealBeforeReserve { get; set; }
		public static ConfigEntry<bool> HealWhenReserveFull { get; set; }
		public static ConfigEntry<float> BaseAbsorbMult { get; set; }
		public static ConfigEntry<float> AddedAbsorbMult { get; set; }
		public static ConfigEntry<float> BaseExportMult { get; set; }
		public static ConfigEntry<float> AddedExportMult { get; set; }
		public static ConfigEntry<bool> RestoreRejuvBehavior { get; set; }
		public static ConfigEntry<float> BaseHealthReserve { get; set; }
		public static ConfigEntry<float> AddedHealthReserve { get; set; }
		public static ConfigEntry<bool> VanillaUsageBehavior { get; set; }
		public static ConfigEntry<float> BaseMaxUsageRate { get; set; }
		public static ConfigEntry<float> StackMaxUsageRate { get; set; }
		public static ConfigEntry<float> BaseMinUsageRate { get; set; }
		public static ConfigEntry<float> StackMinUsageRate { get; set; }



		internal static void Init(ConfigFile Config)
		{
			HealBeforeReserve = Config.Bind(
				"General", "HealBeforeReserve", false,
				"If incoming healing should apply to health before going into reserve."
			);
			HealWhenReserveFull = Config.Bind(
				"General", "HealWhenReserveFull", false,
				"If incoming healing should apply to health after reserve is full."
			);
			BaseAbsorbMult = Config.Bind(
				"General", "BaseAbsorbMult", 2f,
				"Reserve absorbtion. Base effectiveness of healing going into reserve."
			);
			AddedAbsorbMult = Config.Bind(
				"General", "AddedAbsorbMult", 1f,
				"Increased absorption effect per stack."
			);
			BaseExportMult = Config.Bind(
				"General", "BaseExportMult", 1f,
				"Base effectiveness of healing coming out of reserve."
			);
			AddedExportMult = Config.Bind(
				"General", "AddedExportMult", 0f,
				"Increased healing effect per stack."
			);
			RestoreRejuvBehavior = Config.Bind(
				"General", "RestoreRejuvBehavior", false,
				"Most healing multipliers used to apply twice when used with Corpsebloom. Set to true to restore this behavior for Rejuvenation Rack."
			);
			BaseHealthReserve = Config.Bind(
				"General", "BaseHealthReserve", 1.0f,
				"Base reserve gained from health."
			);
			AddedHealthReserve = Config.Bind(
				"General", "AddedHealthReserve", 0.5f,
				"Added reserve gained from health per stack."
			);
			VanillaUsageBehavior = Config.Bind(
				"General", "VanillaUsageBehavior", false,
				"Scale reserve outputs by 1 / stack count."
			);
			BaseMaxUsageRate = Config.Bind(
				"General", "BaseMaxUsageRate", 0.10f,
				"Base maximum healing output from reserve per second."
			);
			StackMaxUsageRate = Config.Bind(
				"General", "StackMaxUsageRate", 0f,
				"Stack maximum healing output from reserve per second."
			);
			BaseMinUsageRate = Config.Bind(
				"General", "BaseMinUsageRate", 0.05f,
				"Base minimum healing output from reserve per second. Set to 0 to disable reserve decay."
			);
			StackMinUsageRate = Config.Bind(
				"General", "StackMinUsageRate", 0f,
				"Stack minimum healing output from reserve per second."
			);

			if (BaseAbsorbMult.Value < 0.1f) BaseAbsorbMult.Value = 0.1f;

			if (BaseExportMult.Value < 0.1f) BaseExportMult.Value = 0.1f;

			if (BaseHealthReserve.Value < 0.1f) BaseHealthReserve.Value = 0.1f;

			if (BaseMaxUsageRate.Value < 0.01f) BaseMaxUsageRate.Value = 0.01f;

			if (BaseMinUsageRate.Value < 0f) BaseMinUsageRate.Value = 0f;
		}
	}
}
