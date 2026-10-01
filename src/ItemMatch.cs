using System;
using System.Collections.Generic;
using System.Text;

namespace SonicPickupFilters
{
	internal static class ItemMatch
	{
		internal static bool MatchesSearch(ItemDrop drop, string pattern)
		{
			if (drop == null)
			{
				return false;
			}

			if (string.IsNullOrWhiteSpace(pattern))
			{
				return true;
			}

			string prefab = PickupRuleStore.NormalizePrefabName(((UnityEngine.Object)drop).name);
			string shared = drop.m_itemData?.m_shared?.m_name ?? string.Empty;
			string localized = GetLocalized(shared);

			return LocateNameMatches(prefab, pattern) ||
			       LocateNameMatches(shared, pattern) ||
			       LocateNameMatches(localized, pattern) ||
			       (shared.StartsWith("$", StringComparison.Ordinal) &&
			        LocateNameMatches(shared.Substring(1), pattern));
		}

		internal static string GetLocalized(string sharedName)
		{
			if (string.IsNullOrEmpty(sharedName))
			{
				return string.Empty;
			}

			if (Localization.instance != null)
			{
				return Localization.instance.Localize(sharedName);
			}

			return sharedName;
		}

		internal static string FriendlyItemTypeLabel(ItemDrop.ItemData.ItemType type)
		{
			switch (type)
			{
				case ItemDrop.ItemData.ItemType.None:
					return "None";
				case ItemDrop.ItemData.ItemType.Material:
					return "Materials";
				case ItemDrop.ItemData.ItemType.Consumable:
					return "Food & Mead";
				case ItemDrop.ItemData.ItemType.OneHandedWeapon:
					return "One-handed weapons";
				case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
					return "Two-handed weapons";
				case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
					return "Two-handed (left)";
				case ItemDrop.ItemData.ItemType.Bow:
					return "Bows";
				case ItemDrop.ItemData.ItemType.Shield:
					return "Shields";
				case ItemDrop.ItemData.ItemType.Helmet:
					return "Helmets";
				case ItemDrop.ItemData.ItemType.Chest:
					return "Chest armor";
				case ItemDrop.ItemData.ItemType.Legs:
					return "Leg armor";
				case ItemDrop.ItemData.ItemType.Hands:
					return "Hands";
				case ItemDrop.ItemData.ItemType.Shoulder:
					return "Capes & shoulders";
				case ItemDrop.ItemData.ItemType.Ammo:
					return "Ammo";
				case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
					return "Thrown / ammo";
				case ItemDrop.ItemData.ItemType.Tool:
					return "Tools";
				case ItemDrop.ItemData.ItemType.Utility:
					return "Utility";
				case ItemDrop.ItemData.ItemType.Torch:
					return "Torches";
				case ItemDrop.ItemData.ItemType.Trophy:
					return "Trophies";
				case ItemDrop.ItemData.ItemType.Misc:
					return "Misc";
				case ItemDrop.ItemData.ItemType.Customization:
					return "Customization";
				case ItemDrop.ItemData.ItemType.Attach_Atgeir:
					return "Atgeir attach";
				case ItemDrop.ItemData.ItemType.Fish:
					return "Fish";
				case ItemDrop.ItemData.ItemType.Trinket:
					return "Trinkets";
				default:
					return SpaceEnumName(type.ToString());
			}
		}

		private static string SpaceEnumName(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return value;
			}

			StringBuilder sb = new StringBuilder(value.Length + 8);
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if (i > 0 && char.IsUpper(c) && !char.IsUpper(value[i - 1]))
				{
					sb.Append(' ');
				}

				sb.Append(c);
			}

			return sb.ToString();
		}

		private static bool LocateNameMatches(string value, string pattern)
		{
			if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern))
			{
				return false;
			}

			if (NameMatches(value, pattern))
			{
				return true;
			}

			string needle = pattern.Replace("*", string.Empty).Trim();
			if (needle.Length == 0)
			{
				return false;
			}

			return value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private static bool NameMatches(string value, string pattern)
		{
			if (string.IsNullOrEmpty(pattern))
			{
				return false;
			}

			if (pattern.IndexOf('*') < 0)
			{
				return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
			}

			string[] parts = pattern.Split('*');
			int index = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i];
				if (part.Length == 0)
				{
					continue;
				}

				if (i == 0)
				{
					if (!value.StartsWith(part, StringComparison.OrdinalIgnoreCase))
					{
						return false;
					}

					index = part.Length;
					continue;
				}

				bool isLast = i == parts.Length - 1;
				if (isLast && pattern[pattern.Length - 1] != '*')
				{
					if (!value.EndsWith(part, StringComparison.OrdinalIgnoreCase))
					{
						return false;
					}

					return value.Length - part.Length >= index;
				}

				int found = value.IndexOf(part, index, StringComparison.OrdinalIgnoreCase);
				if (found < 0)
				{
					return false;
				}

				index = found + part.Length;
			}

			return true;
		}
	}
}
