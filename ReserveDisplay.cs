using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TPDespair.CorpseBloomReborn
{
	public class BaseReserveDisplay : MonoBehaviour
	{
		public HealthBar healthBar;

		private GameObject reserveContainer;
		private RectTransform containerTransform;
		private GameObject reserveBar;
		private RectTransform barTransform;

		private float reserveFraction = 0f;
		private float displayScale = 0f;

		private bool rebuild = true;
		private float rebuildTimer = 0.25f;



		public void UpdateReserveDisplay()
		{
			UpdateDisplayValues();
			UpdateContainer();
			UpdateDisplay();
		}

		public void UpdateDisplayValues()
		{
			reserveFraction = 0f;

			if (healthBar)
			{
				HealthComponent healthComponent = healthBar.source;
				if (healthComponent)
				{
					CharacterBody body = healthComponent.body;
					if (body)
					{
						int buffCount = body.GetBuffCount(CorpseBloomRebornContent.Buffs.CBReserve);
						if (buffCount > 0)
						{
							reserveFraction = Mathf.Clamp(buffCount / 100f, 0f, 1f);
							displayScale = (1f / body.cursePenalty) * (healthComponent.fullHealth / healthComponent.fullCombinedHealth);
						}
					}
				}
			}
		}

		public void UpdateContainer()
		{
			if (reserveContainer && rebuild)
			{
				rebuildTimer -= Time.deltaTime;
				if (rebuildTimer <= 0f)
				{
					rebuildTimer = 0.25f;
					rebuild = false;

					DestroyReserveBar();
				}
			}

			// reserve fraction > 0 implies that we have a health bar to attach to
			if (reserveFraction > 0f && !reserveContainer)
			{
				CreateReserveBar();
			}
		}

		public void UpdateDisplay()
		{
			if (reserveContainer)
			{
				if (reserveFraction > 0f)
				{
					float dispValue = -0.5f + reserveFraction * displayScale;
					barTransform.anchorMax = new Vector2(dispValue, 0.5f);

					if (reserveContainer.activeSelf == false) reserveContainer.SetActive(true);
				}
				else
				{
					if (reserveContainer.activeSelf == true) reserveContainer.SetActive(false);
				}
			}
		}



		public void RequestRebuild()
		{
			rebuildTimer = 0.25f;
			rebuild = true;
		}

		private void DestroyReserveBar()
		{
			reserveContainer.SetActive(false);
			Destroy(reserveContainer);

			reserveContainer = null;
			containerTransform = null;
			reserveBar = null;
			barTransform = null;
		}

		private void CreateReserveBar()
		{
			Rect rect = healthBar.barContainer.rect;
			//Debug.LogWarning("CreateReserveBar : " + rect.width + " x " + rect.height);
			float width = rect.width;
			float height = Mathf.CeilToInt(rect.height / 3.125f);
			float halfWidth = width / 2f;

			reserveContainer = new GameObject("ReserveRect");
			containerTransform = reserveContainer.AddComponent<RectTransform>();
			containerTransform.position = new Vector3(0f, 0f);
			containerTransform.anchoredPosition = new Vector2(halfWidth, 0f);
			containerTransform.anchorMin = new Vector2(0f, 0f);
			containerTransform.anchorMax = new Vector2(0f, 0f);
			containerTransform.offsetMin = new Vector2(halfWidth, 0f);
			containerTransform.offsetMax = new Vector2(halfWidth, height);
			containerTransform.sizeDelta = new Vector2(width, height);
			containerTransform.pivot = new Vector2(0f, 0f);

			reserveBar = new GameObject("ReserveBar");
			reserveBar.transform.SetParent(containerTransform.transform);
			barTransform = reserveBar.AddComponent<RectTransform>();
			barTransform.sizeDelta = new Vector2(width, height);
			barTransform.pivot = new Vector2(0.5f, 1.0f);
			reserveBar.AddComponent<Image>().color = new Color(0.625f, 0.25f, 1f, 0.65f);

			reserveContainer.transform.SetParent(healthBar.transform, false);
		}
	}

	public class HudReserveDisplay : BaseReserveDisplay
	{
		public HUD hud;

		public void Update()
		{
			if (hud)
			{
				healthBar = hud.healthBar;
			}

			UpdateReserveDisplay();
		}
	}

	public class AllyReserveDisplay : BaseReserveDisplay
	{
		public AllyCardController controller;
		public bool indented = false;

		public void Update()
		{
			if (controller)
			{
				healthBar = controller.healthBar;

				bool shouldIndent = controller.shouldIndent;
				if (indented != shouldIndent)
				{
					RequestRebuild();
					indented = shouldIndent;
				}
			}

			UpdateReserveDisplay();
		}
	}
}
