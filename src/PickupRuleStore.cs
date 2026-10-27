using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;

namespace SonicPickupFilters
{
	internal enum PickupRuleMode
	{
		Allow = 0,
		Block = 1,
		Cap = 2
	}

	internal readonly struct PickupRule
	{
		internal readonly PickupRuleMode Mode;

		internal readonly int Cap;

		internal PickupRule(PickupRuleMode mode, int cap)
		{
			Mode = mode;
			Cap = cap;
		}

		internal static PickupRule Allow => new PickupRule(PickupRuleMode.Allow, 0);

		internal static PickupRule Block => new PickupRule(PickupRuleMode.Block, 0);

		internal static PickupRule CapAt(int amount)
		{
			if (amount < 1)
			{
				return Block;
			}

			return new PickupRule(PickupRuleMode.Cap, amount);
		}
	}

	internal static class PickupRuleStore
	{
		private static ConfigEntry<string> _config;

		private static readonly Dictionary<string, PickupRule> Rules =
			new Dictionary<string, PickupRule>(StringComparer.OrdinalIgnoreCase);

		internal static void Init(ConfigEntry<string> config)
		{
			_config = config;
			ReloadFromConfig();
		}

		internal static void ReloadFromConfig()
		{
			Rules.Clear();
			if (_config == null || string.IsNullOrWhiteSpace(_config.Value))
			{
				return;
			}

			string[] parts = _config.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				if (part.Length == 0)
				{
					continue;
				}

				int colon = part.LastIndexOf(':');
				if (colon <= 0 || colon >= part.Length - 1)
				{
					SonicPickupFiltersPlugin.DebugLog("Ignoring malformed rule entry: " + part);
					continue;
				}

				string prefab = part.Substring(0, colon).Trim();
				string amountText = part.Substring(colon + 1).Trim();
				if (prefab.Length == 0 || !int.TryParse(amountText, out int amount) || amount < 0)
				{
					SonicPickupFiltersPlugin.DebugLog("Ignoring invalid rule entry: " + part);
					continue;
				}

				Rules[prefab] = amount == 0 ? PickupRule.Block : PickupRule.CapAt(amount);
			}

			SonicPickupFiltersPlugin.DebugLog(
				"Loaded " + Rules.Count + " pickup rules: " + FormatRulesSnapshot());
		}

		/// <summary>Compact dump for Cap/AutoPickup logs, e.g. "Wood:Cap10,GreydwarfEye:Block".</summary>
		internal static string FormatRulesSnapshot()
		{
			if (Rules.Count == 0)
			{
				return "(none)";
			}

			StringBuilder sb = new StringBuilder();
			bool first = true;
			foreach (KeyValuePair<string, PickupRule> pair in Rules)
			{
				if (!first)
				{
					sb.Append(',');
				}

				first = false;
				sb.Append(pair.Key).Append(':');
				if (pair.Value.Mode == PickupRuleMode.Block)
				{
					sb.Append("Block");
				}
				else if (pair.Value.Mode == PickupRuleMode.Cap)
				{
					sb.Append("Cap").Append(pair.Value.Cap);
				}
				else
				{
					sb.Append("Allow");
				}
			}

			return sb.ToString();
		}

		internal static PickupRule GetRule(string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName))
			{
				return PickupRule.Allow;
			}

			if (Rules.TryGetValue(NormalizePrefabName(prefabName), out PickupRule rule))
			{
				return rule;
			}

			return PickupRule.Allow;
		}

		internal static IEnumerable<KeyValuePair<string, PickupRule>> EnumerateRules()
		{
			return Rules;
		}

		internal static int CountByMode(PickupRuleMode mode)
		{
			int count = 0;
			foreach (KeyValuePair<string, PickupRule> pair in Rules)
			{
				if (pair.Value.Mode == mode)
				{
					count++;
				}
			}

			return count;
		}

		internal static void SetRule(string prefabName, PickupRule rule)
		{
			prefabName = NormalizePrefabName(prefabName);
			if (string.IsNullOrEmpty(prefabName))
			{
				return;
			}

			if (rule.Mode == PickupRuleMode.Allow)
			{
				Rules.Remove(prefabName);
				SonicPickupFiltersPlugin.DebugLog("SetRule " + prefabName + " → Allow (removed)");
			}
			else if (rule.Mode == PickupRuleMode.Block)
			{
				Rules[prefabName] = PickupRule.Block;
				SonicPickupFiltersPlugin.DebugLog("SetRule " + prefabName + " → Block");
			}
			else
			{
				Rules[prefabName] = PickupRule.CapAt(rule.Cap);
				SonicPickupFiltersPlugin.DebugLog("SetRule " + prefabName + " → Cap " + rule.Cap);
			}

			Save();
		}

		internal static void CycleRule(string prefabName, int defaultCap)
		{
			PickupRule current = GetRule(prefabName);
			PickupRule next;
			switch (current.Mode)
			{
				case PickupRuleMode.Allow:
					next = PickupRule.CapAt(Math.Max(1, defaultCap));
					break;
				case PickupRuleMode.Cap:
					next = PickupRule.Block;
					break;
				default:
					next = PickupRule.Allow;
					break;
			}

			SetRule(prefabName, next);
		}

		internal static void AdjustCap(string prefabName, int delta, int defaultCap)
		{
			PickupRule current = GetRule(prefabName);
			int baseCap = current.Mode == PickupRuleMode.Cap ? current.Cap : Math.Max(1, defaultCap);
			int next = baseCap + delta;
			if (next < 1)
			{
				SetRule(prefabName, PickupRule.Allow);
				return;
			}

			SetRule(prefabName, PickupRule.CapAt(next));
		}

		/// <summary>Inventory UI helper. AutoPickup Cap logic lives in AutoPickupPatches (count + suppress only).</summary>
		internal static bool ShouldAllowAutoPickup(Player player, ItemDrop drop, string prefabName)
		{
			PickupRule rule = GetRule(prefabName);
			if (rule.Mode == PickupRuleMode.Allow)
			{
				return true;
			}

			if (rule.Mode == PickupRuleMode.Block)
			{
				return false;
			}

			if ((object)player == null || (object)drop == null)
			{
				return false;
			}

			// Avoid Player.GetInventory() — throws InvalidCast on current client; Cap path uses m_inventory field.
			Inventory inventory = null;
			try
			{
				var field = HarmonyLib.AccessTools.Field(typeof(Humanoid), "m_inventory");
				inventory = field?.GetValue(player) as Inventory;
			}
			catch
			{
				return false;
			}

			if ((object)inventory == null)
			{
				return false;
			}

			string shared = drop.m_itemData?.m_shared?.m_name;
			if (string.IsNullOrEmpty(shared))
			{
				return false;
			}

			int have = inventory.CountItems(shared, -1, true);
			int incoming = drop.m_itemData != null ? Math.Max(1, drop.m_itemData.m_stack) : 1;
			bool allow = have + incoming <= rule.Cap;
			SonicPickupFiltersPlugin.DebugLog(
				$"ShouldAllow Cap prefab={prefabName} have={have} incoming={incoming} cap={rule.Cap} allow={allow}");
			return allow;
		}

		internal static string NormalizePrefabName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return string.Empty;
			}

			string trimmed = name.Trim();
			const string clone = "(Clone)";
			if (trimmed.EndsWith(clone, StringComparison.Ordinal))
			{
				trimmed = trimmed.Substring(0, trimmed.Length - clone.Length).Trim();
			}

			return trimmed;
		}

		private static void Save()
		{
			if (_config == null)
			{
				return;
			}

			StringBuilder sb = new StringBuilder();
			bool first = true;
			foreach (KeyValuePair<string, PickupRule> pair in Rules)
			{
				if (!first)
				{
					sb.Append(',');
				}

				first = false;
				int value = pair.Value.Mode == PickupRuleMode.Block ? 0 : pair.Value.Cap;
				sb.Append(pair.Key).Append(':').Append(value);
			}

			_config.Value = sb.ToString();
			SonicPickupFiltersPlugin.AcknowledgeConfigWrite();
		}
	}
}
