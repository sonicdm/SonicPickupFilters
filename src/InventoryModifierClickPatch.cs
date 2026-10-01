using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SonicPickupFilters
{
	/// <summary>
	/// Hold the configured modifier and right-click an inventory item to toggle Block.
	/// </summary>
	[HarmonyPatch(typeof(InventoryGui), "OnRightClickItem")]
	internal static class InventoryModifierClickPatch
	{
		[HarmonyPrefix]
		private static bool Prefix(InventoryGrid grid, ItemDrop.ItemData item, Vector2i pos)
		{
			if (!SonicPickupFiltersPlugin.IsModEnabled())
			{
				return true;
			}

			if (!IsBlockToggleModifierHeld())
			{
				return true;
			}

			InventoryGui gui = InventoryGui.instance;
			if ((Object)(object)gui == (Object)null ||
			    (Object)(object)gui.m_playerGrid == (Object)null ||
			    (Object)(object)grid != (Object)(object)gui.m_playerGrid)
			{
				return true;
			}

			if (item == null || item.m_shared == null)
			{
				return true;
			}

			string prefab = ResolvePrefabName(item);
			if (string.IsNullOrEmpty(prefab))
			{
				return true;
			}

			PickupRule current = PickupRuleStore.GetRule(prefab);
			if (current.Mode == PickupRuleMode.Block)
			{
				PickupRuleStore.SetRule(prefab, PickupRule.Allow);
				Message(FormatName(item) + ": auto-pickup allowed");
			}
			else
			{
				PickupRuleStore.SetRule(prefab, PickupRule.Block);
				Message(FormatName(item) + ": auto-pickup blocked");
			}

			PickupFilterPanel.RefreshIfOpen();
			return false;
		}

		internal static bool IsBlockToggleModifierHeld()
		{
			ConfigEntry<KeyboardShortcut> shortcut = SonicPickupFiltersPlugin.InventoryBlockModifier;
			if (shortcut == null)
			{
				return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
			}

			KeyboardShortcut value = shortcut.Value;
			if (value.Equals(KeyboardShortcut.Empty))
			{
				return false;
			}

			return value.IsPressed();
		}

		private static string FormatName(ItemDrop.ItemData item)
		{
			if (Localization.instance != null && item.m_shared != null)
			{
				return Localization.instance.Localize(item.m_shared.m_name);
			}

			return item.m_shared?.m_name ?? "?";
		}

		private static string ResolvePrefabName(ItemDrop.ItemData item)
		{
			if ((Object)(object)item.m_dropPrefab != (Object)null)
			{
				return PickupRuleStore.NormalizePrefabName(((Object)item.m_dropPrefab).name);
			}

			if ((Object)(object)ObjectDB.instance == (Object)null || item.m_shared == null)
			{
				return string.Empty;
			}

			foreach (GameObject go in ObjectDB.instance.m_items)
			{
				if ((Object)(object)go == (Object)null)
				{
					continue;
				}

				ItemDrop drop = go.GetComponent<ItemDrop>();
				if ((Object)(object)drop == (Object)null || drop.m_itemData?.m_shared == null)
				{
					continue;
				}

				if (drop.m_itemData.m_shared.m_name == item.m_shared.m_name)
				{
					return PickupRuleStore.NormalizePrefabName(((Object)go).name);
				}
			}

			return string.Empty;
		}

		private static void Message(string text)
		{
			Player player = Player.m_localPlayer;
			if ((Object)(object)player != (Object)null)
			{
				player.Message(MessageHud.MessageType.TopLeft, text);
			}
		}
	}
}
