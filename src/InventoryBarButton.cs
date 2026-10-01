using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SonicPickupFilters
{
	[HarmonyPatch(typeof(InventoryGui), "Awake")]
	internal static class InventoryBarButton
	{
		private const string ButtonName = "sonic_pickupfilter_button";

		private const string IconItemPrefab = "Coins";

		private static readonly Vector3[] Corners = new Vector3[4];

		[HarmonyPostfix]
		private static void Postfix(InventoryGui __instance)
		{
			TryInject(__instance);
		}

		/// <summary>
		/// ScriptEngine reload happens after InventoryGui.Awake — call this from plugin Awake when GUI already exists.
		/// </summary>
		internal static void TryInjectExisting()
		{
			InventoryGui gui = InventoryGui.instance;
			if ((Object)(object)gui != (Object)null)
			{
				TryInject(gui);
			}
		}

		internal static void DestroyInjected()
		{
			InventoryGui gui = InventoryGui.instance;
			if ((Object)(object)gui == (Object)null || (Object)(object)gui.m_pvp == (Object)null)
			{
				return;
			}

			Transform parent = ((Component)gui.m_pvp).transform.parent;
			Transform existing = parent.Find(ButtonName);
			if ((Object)(object)existing != (Object)null)
			{
				Object.Destroy(((Component)existing).gameObject);
			}
		}

		internal static void TryInject(InventoryGui __instance)
		{
			if (!SonicPickupFiltersPlugin.IsModEnabled() || GUIManagerMissing())
			{
				return;
			}

			if ((Object)(object)__instance == (Object)null)
			{
				return;
			}

			Toggle pvp = __instance.m_pvp;
			if ((Object)(object)pvp == (Object)null)
			{
				SonicPickupFiltersPlugin.DebugLog("m_pvp missing; skip Pickup Filter inventory button.");
				return;
			}

			Transform parent = ((Component)pvp).transform.parent;
			Transform existing = parent.Find(ButtonName);
			if ((Object)(object)existing != (Object)null)
			{
				return;
			}

			Button trophies = FindTrophiesButton(parent, pvp);
			if ((Object)(object)trophies == (Object)null)
			{
				SonicPickupFiltersPlugin.DebugLog("Trophies button not found; skip Pickup Filter inventory button.");
				return;
			}

			GameObject clone = Object.Instantiate(((Component)trophies).gameObject, parent);
			((Object)clone).name = ButtonName;
			Button button = clone.GetComponent<Button>();
			button.onClick = new Button.ButtonClickedEvent();
			button.onClick.AddListener(PickupFilterPanel.TogglePanel);

			string label = ModLocalization.Translate(ModLocalization.Button, "Pickup Filter");
			TMP_Text tmp = clone.GetComponentInChildren<TMP_Text>();
			if ((Object)(object)tmp != (Object)null)
			{
				tmp.text = label;
			}
			else
			{
				Text text = clone.GetComponentInChildren<Text>();
				if ((Object)(object)text != (Object)null)
				{
					text.text = label;
				}
			}

			UITooltip tip = clone.GetComponentInChildren<UITooltip>();
			if ((Object)(object)tip != (Object)null)
			{
				tip.m_topic = string.Empty;
				tip.m_text = label;
			}

			Image icon = FindIconImage(clone);
			if ((Object)(object)icon != (Object)null)
			{
				if (button.transition == Selectable.Transition.SpriteSwap &&
				    (Object)(object)button.targetGraphic == (Object)(object)icon)
				{
					button.transition = Selectable.Transition.ColorTint;
				}

				((MonoBehaviour)__instance).StartCoroutine(SwapIconWhenReady(icon));
			}

			LayoutRow(parent, ((Component)trophies).GetComponent<RectTransform>(), clone.GetComponent<RectTransform>());
			SonicPickupFiltersPlugin.DebugLog("Pickup Filter inventory button injected.");
		}

		private static bool GUIManagerMissing()
		{
			try
			{
				return Jotunn.Managers.GUIManager.IsHeadless();
			}
			catch
			{
				return false;
			}
		}

		private static Button FindTrophiesButton(Transform bar, Toggle pvpToggle)
		{
			Button best = null;
			float bestGap = float.MaxValue;
			float pvpLeft = WorldLeft(((Component)pvpToggle).GetComponent<RectTransform>());
			foreach (Transform child in bar)
			{
				Button button = ((Component)child).GetComponent<Button>();
				if ((Object)(object)button == (Object)null)
				{
					continue;
				}

				if (((Object)child).name.IndexOf("Trophies", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return button;
				}

				float gap = pvpLeft - WorldRight((RectTransform)child);
				if (gap > 0f && gap < bestGap)
				{
					bestGap = gap;
					best = button;
				}
			}

			return best;
		}

		private static Image FindIconImage(GameObject buttonGo)
		{
			Image last = null;
			Image[] images = buttonGo.GetComponentsInChildren<Image>(true);
			for (int i = 0; i < images.Length; i++)
			{
				if ((Object)(object)((Component)images[i]).gameObject != (Object)(object)buttonGo)
				{
					last = images[i];
				}
			}

			return (Object)(object)last != (Object)null ? last : buttonGo.GetComponent<Image>();
		}

		private static IEnumerator SwapIconWhenReady(Image iconImage)
		{
			while ((Object)(object)ObjectDB.instance == (Object)null || ObjectDB.instance.m_items.Count == 0)
			{
				yield return null;
			}

			if ((Object)(object)iconImage == (Object)null)
			{
				yield break;
			}

			GameObject prefab = ObjectDB.instance.GetItemPrefab(IconItemPrefab);
			Sprite sprite = null;
			if ((Object)(object)prefab != (Object)null)
			{
				ItemDrop drop = prefab.GetComponent<ItemDrop>();
				if ((Object)(object)drop != (Object)null && drop.m_itemData != null)
				{
					sprite = drop.m_itemData.GetIcon();
				}
			}

			if ((Object)(object)sprite != (Object)null)
			{
				iconImage.sprite = sprite;
			}
		}

		private static void LayoutRow(Transform bar, RectTransform trophiesRt, RectTransform insertedRt)
		{
			List<RectTransform> buttons = new List<RectTransform>();
			foreach (Transform child in bar)
			{
				if (!((Component)child).gameObject.activeSelf ||
				    (Object)(object)child == (Object)(object)((Component)insertedRt).transform ||
				    (Object)(object)((Component)child).GetComponent<Selectable>() == (Object)null)
				{
					continue;
				}

				buttons.Add((RectTransform)child);
			}

			if (buttons.Count == 0)
			{
				return;
			}

			buttons.Sort((a, b) => WorldLeft(a).CompareTo(WorldLeft(b)));
			float scale = bar.lossyScale.x;
			if (scale <= 0f)
			{
				scale = 1f;
			}

			List<float> gaps = new List<float>();
			for (int i = 1; i < buttons.Count; i++)
			{
				gaps.Add(WorldLeft(buttons[i]) - WorldRight(buttons[i - 1]));
			}

			gaps.Sort();
			float medianGap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 8f * scale;

			int trophiesIndex = buttons.IndexOf(trophiesRt);
			buttons.Insert(trophiesIndex >= 0 ? trophiesIndex + 1 : buttons.Count, insertedRt);

			float totalWidth = 0f;
			for (int i = 0; i < buttons.Count; i++)
			{
				totalWidth += WorldRight(buttons[i]) - WorldLeft(buttons[i]);
			}

			RectTransform barRt = (RectTransform)bar;
			float left = WorldLeft(barRt);
			float right = WorldRight(barRt);
			float span = right - left;
			float side = Mathf.Min(8f * scale, Mathf.Max(0f, (span - totalWidth) * 0.5f));
			float free = buttons.Count > 1 ? (span - 2f * side - totalWidth) / (buttons.Count - 1) : 0f;
			float gap = Mathf.Max(0f, Mathf.Min(medianGap, free));
			float cursor = right - side;
			if (span < totalWidth)
			{
				cursor = WorldRight(buttons[buttons.Count - 1]);
			}

			for (int i = buttons.Count - 1; i >= 0; i--)
			{
				float w = WorldRight(buttons[i]) - WorldLeft(buttons[i]);
				float targetLeft = cursor - w;
				Vector2 anchored = buttons[i].anchoredPosition;
				anchored.x += (targetLeft - WorldLeft(buttons[i])) / scale;
				buttons[i].anchoredPosition = anchored;
				cursor = targetLeft - gap;
			}
		}

		private static float WorldLeft(RectTransform rt)
		{
			rt.GetWorldCorners(Corners);
			return Corners[0].x;
		}

		private static float WorldRight(RectTransform rt)
		{
			rt.GetWorldCorners(Corners);
			return Corners[2].x;
		}
	}
}
