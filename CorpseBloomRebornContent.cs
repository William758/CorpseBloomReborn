using RoR2;
using RoR2.ContentManagement;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TPDespair.CorpseBloomReborn
{
	public class CorpseBloomRebornContent : IContentPackProvider
	{
		public ContentPack contentPack = new ContentPack();

		public string identifier
		{
			get { return "CorpseBloomRebornContent"; }
		}

		public IEnumerator LoadStaticContentAsync(LoadStaticContentAsyncArgs args)
		{
			Buffs.Create();

			ReserveManager.IndicatorBuff = Buffs.CBReserve;

			contentPack.buffDefs.Add(Buffs.buffDefs.ToArray());

			args.ReportProgress(1f);
			yield break;
		}

		public IEnumerator GenerateContentPackAsync(GetContentPackAsyncArgs args)
		{
			ContentPack.Copy(contentPack, args.output);
			args.ReportProgress(1f);
			yield break;
		}

		public IEnumerator FinalizeAsync(FinalizeAsyncArgs args)
		{
			args.ReportProgress(1f);
			yield break;
		}



		public static class Buffs
		{
			public static BuffDef CBReserve;

			public static List<BuffDef> buffDefs = new List<BuffDef>();


			public static void Create()
			{
				CBReserve = ScriptableObject.CreateInstance<BuffDef>();
				CBReserve.name = "CBReserve";
				CBReserve.buffColor = new Color(0.65f, 0.35f, 1f);
				CBReserve.canStack = true;
				CBReserve.isDebuff = false;
				CBReserve.iconSprite = LegacyResourcesAPI.Load<BuffDef>("BuffDefs/MedkitHeal").iconSprite;

				buffDefs.Add(CBReserve);
			}
		}
	}
}