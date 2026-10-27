using System.Collections.Generic;
using Jotunn.Entities;
using Jotunn.Managers;

namespace SonicPickupFilters
{
	internal static class ModLocalization
	{
		internal const string Title = "$sonic_pickupfilters_title";

		internal const string Hint = "$sonic_pickupfilters_hint";

		internal const string SearchPlaceholder = "$sonic_pickupfilters_search";

		internal const string CategoryAll = "$sonic_pickupfilters_cat_all";

		internal const string Clear = "$sonic_pickupfilters_clear";

		internal const string TabAll = "$sonic_pickupfilters_tab_all";

		internal const string TabBlocked = "$sonic_pickupfilters_tab_blocked";

		internal const string TabCapped = "$sonic_pickupfilters_tab_capped";

		internal const string Close = "$sonic_pickupfilters_close";

		internal const string Button = "$sonic_pickupfilters_button";

		internal const string ColItem = "$sonic_pickupfilters_col_item";

		internal const string ColBlock = "$sonic_pickupfilters_col_block";

		internal const string ColCap = "$sonic_pickupfilters_col_cap";

		internal const string ColCapQty = "$sonic_pickupfilters_col_cap_qty";

		internal const string TipBlockTopic = "$sonic_pickupfilters_tip_block_topic";

		internal const string TipBlockBody = "$sonic_pickupfilters_tip_block_body";

		internal const string TipCapTopic = "$sonic_pickupfilters_tip_cap_topic";

		internal const string TipCapBody = "$sonic_pickupfilters_tip_cap_body";

		internal const string TipCapQtyTopic = "$sonic_pickupfilters_tip_cap_qty_topic";

		internal const string TipCapQtyBody = "$sonic_pickupfilters_tip_cap_qty_body";

		internal const string TipItemTopic = "$sonic_pickupfilters_tip_item_topic";

		internal const string TipItemBody = "$sonic_pickupfilters_tip_item_body";

		internal const string CapPlaceholderOff = "$sonic_pickupfilters_cap_ph_off";

		internal const string CapPlaceholderOn = "$sonic_pickupfilters_cap_ph_on";

		private static CustomLocalization _localization;

		internal static void Register()
		{
			if (LocalizationManager.Instance == null)
			{
				return;
			}

			_localization = LocalizationManager.Instance.GetLocalization();
			_localization.AddTranslation("English", new Dictionary<string, string>
			{
				{ "sonic_pickupfilters_title", "Pickup Filter" },
				{
					"sonic_pickupfilters_hint",
					"Block = never auto-pick. Cap = pick until the count (check Cap to start at max stack, then edit the number)."
				},
				{ "sonic_pickupfilters_search", "Search items…" },
				{ "sonic_pickupfilters_cat_all", "All categories" },
				{ "sonic_pickupfilters_clear", "Clear" },
				{ "sonic_pickupfilters_tab_all", "All" },
				{ "sonic_pickupfilters_tab_blocked", "Blocked" },
				{ "sonic_pickupfilters_tab_capped", "Capped" },
				{ "sonic_pickupfilters_close", "Close" },
				{ "sonic_pickupfilters_button", "Pickup Filter" },
				{ "sonic_pickupfilters_col_item", "Item" },
				{ "sonic_pickupfilters_col_block", "Block" },
				{ "sonic_pickupfilters_col_cap", "Cap" },
				{ "sonic_pickupfilters_col_cap_qty", "Qty" },
				{ "sonic_pickupfilters_tip_block_topic", "Block" },
				{
					"sonic_pickupfilters_tip_block_body",
					"Never auto-pick this item.\nManual Use still works.\nTurning Block on turns Cap off."
				},
				{ "sonic_pickupfilters_tip_cap_topic", "Cap" },
				{
					"sonic_pickupfilters_tip_cap_body",
					"Check Cap to limit auto-pickup.\nFills Qty with max stack.\nTurning Cap on turns Block off."
				},
				{ "sonic_pickupfilters_tip_cap_qty_topic", "Qty" },
				{
					"sonic_pickupfilters_tip_cap_qty_body",
					"Max auto-pickup count while Cap is checked.\nEmpty/invalid restores max stack."
				},
				{ "sonic_pickupfilters_tip_item_topic", "Item" },
				{
					"sonic_pickupfilters_tip_item_body",
					"Discovered items from your world. Use Block or Cap to control auto-pickup. Manual Use still works when blocked."
				},
				{ "sonic_pickupfilters_cap_ph_off", "—" },
				{ "sonic_pickupfilters_cap_ph_on", "max" }
			});
		}

		internal static string Translate(string token, string fallback)
		{
			if (_localization != null)
			{
				string value = _localization.TryTranslate(token);
				if (!string.IsNullOrEmpty(value) && value[0] != '[' && value != token)
				{
					return value;
				}
			}

			if (LocalizationManager.Instance != null)
			{
				string value = LocalizationManager.Instance.TryTranslate(token);
				if (!string.IsNullOrEmpty(value) && value[0] != '[' && value != token)
				{
					return value;
				}
			}

			return fallback;
		}
	}
}
