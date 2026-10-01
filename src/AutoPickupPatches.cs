using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SonicPickupFilters
{
	/// <summary>
	/// AutoPickup filter. Logging uses breadcrumbs (step + item) because Release stacks often
	/// only show Prefix with no line numbers — useless without knowing which call cast-failed.
	/// </summary>
	[HarmonyPatch(typeof(Player), "AutoPickup")]
	internal static class AutoPickupPatches
	{
		private static readonly List<ItemDrop> Suppressed = new List<ItemDrop>(64);

		private static readonly Dictionary<string, int> PendingPickup =
			new Dictionary<string, int>(StringComparer.Ordinal);

		private static Collider[] _overlapBuffer = new Collider[128];

		private static readonly FieldInfo AutoPickupMaskField =
			AccessTools.Field(typeof(Player), "m_autoPickupMask");

		private static float _nextSummaryLog;
		private static float _nextFailLog;
		private static int _passSeq;
		private static int _failLogs;

		private static string _step = "init";
		private static string _item = "-";

		/// <summary>On ScriptEngine reload / plugin destroy — clear any stuck suppress flags.</summary>
		internal static void ResetRuntimeState()
		{
			try
			{
				RestoreSuppressed();
			}
			catch
			{
				Suppressed.Clear();
			}

			PendingPickup.Clear();
			_step = "reset";
			_item = "-";
		}

		/// <summary>
		/// One-shot Cap/nearby dump for console (<c>spf_dump</c>) — no game restart needed to inspect state.
		/// </summary>
		internal static string DumpNearby(Player player)
		{
			StringBuilder sb = new StringBuilder(1024);
			sb.AppendLine("=== spf_dump ===");
			sb.AppendLine("rules=" + PickupRuleStore.FormatRulesSnapshot());
			sb.AppendLine("enabled=" + SonicPickupFiltersPlugin.IsModEnabled() +
			              " debug=" + SonicPickupFiltersPlugin.DebugEnabled);

			if ((Object)(object)player == (Object)null)
			{
				sb.AppendLine("player=null");
				return sb.ToString();
			}

			object inventory = TryGetInventoryObject(player, out string invHow);
			sb.AppendLine("inventory=" + (inventory != null ? "ok" : "null") + " via=" + invHow);

			Vector3 center = ((Component)player).transform.position + Vector3.up * 0.5f;
			float range = player.m_autoPickupRange;
			int mask = ResolveMask(player);
			int hitCount = mask >= 0
				? Physics.OverlapSphereNonAlloc(center, range, _overlapBuffer, mask)
				: Physics.OverlapSphereNonAlloc(center, range, _overlapBuffer);

			sb.AppendLine($"range={range:0.##} mask={mask} hits={hitCount}");

			int listed = 0;
			for (int i = 0; i < hitCount; i++)
			{
				Collider col = _overlapBuffer[i];
				if ((Object)(object)col == (Object)null)
				{
					continue;
				}

				ItemDrop drop = null;
				string colName = "?";
				try
				{
					colName = ((Object)col.gameObject).name;
					drop = col.GetComponent<ItemDrop>();
					if ((Object)(object)drop == (Object)null && (Object)(object)col.attachedRigidbody != (Object)null)
					{
						drop = col.attachedRigidbody.GetComponent<ItemDrop>() ??
						       col.attachedRigidbody.GetComponentInParent<ItemDrop>();
					}
				}
				catch (Exception ex)
				{
					sb.AppendLine($"  hit[{i}] col='{colName}' GetItemDrop FAIL {ex.GetType().Name}: {ex.Message}");
					continue;
				}

				if ((Object)(object)drop == (Object)null)
				{
					continue;
				}

				listed++;
				string raw;
				try
				{
					raw = ((Object)((Component)drop).gameObject).name;
				}
				catch
				{
					raw = colName;
				}

				string prefab = PickupRuleStore.NormalizePrefabName(raw);
				PickupRule rule = PickupRuleStore.GetRule(prefab);
				string shared = TryShared(drop, prefab);
				int stack = TryStack(drop, prefab);
				int maxStack = TryMaxStack(drop, prefab);
				bool auto = false;
				try
				{
					auto = drop.m_autoPickup;
				}
				catch (Exception ex)
				{
					sb.AppendLine($"  {prefab} m_autoPickup FAIL {ex.GetType().Name}: {ex.Message}");
					continue;
				}

				string haveText = "-";
				if (!string.IsNullOrEmpty(shared) && inventory != null)
				{
					int have = CountHaveReflect(inventory, shared, prefab, out string via);
					haveText = have + " via " + via;
				}

				sb.AppendLine(
					$"  {prefab} raw='{raw}' rule={FormatRule(rule)} auto={auto} " +
					$"shared='{shared}' stack={stack} max={maxStack} invHave={haveText}");
			}

			sb.AppendLine("dropsListed=" + listed);
			return sb.ToString();
		}

		[HarmonyPrefix]
		private static void Prefix(Player __instance, float dt)
		{
			_step = "Prefix.enter";
			_item = "-";
			try
			{
				RunPrefix(__instance);
				_step = "Prefix.done";
			}
			catch (Exception ex)
			{
				Fail("Prefix.uncaught", ex);
				try
				{
					RestoreSuppressed();
				}
				catch (Exception rex)
				{
					Fail("Prefix.restoreAfterFail", rex);
					Suppressed.Clear();
				}

				PendingPickup.Clear();
			}
		}

		private static void RunPrefix(Player __instance)
		{
			_step = "RestoreSuppressed";
			RestoreSuppressed();
			PendingPickup.Clear();

			_step = "IsModEnabled";
			if (!SonicPickupFiltersPlugin.IsModEnabled())
			{
				return;
			}

			_step = "LocalPlayerCheck";
			if ((Object)(object)__instance == (Object)null ||
			    (Object)(object)Player.m_localPlayer == (Object)null ||
			    (Object)(object)__instance != (Object)(object)Player.m_localPlayer)
			{
				return;
			}

			// Do NOT resolve inventory up-front. Block needs none; GetInventory/m_inventory
			// InvalidCast under ScriptEngine / typed FieldInfo — Cap resolves lazily via runtime reflection.

			_step = "ReadRange";
			Vector3 center = ((Component)__instance).transform.position + Vector3.up * 0.5f;
			float range = __instance.m_autoPickupRange;

			_step = "ResolveMask";
			int mask = ResolveMask(__instance);

			_step = "OverlapSphere";
			_item = $"range={range:0.##} mask={mask}";
			int hitCount = mask >= 0
				? Physics.OverlapSphereNonAlloc(center, range, _overlapBuffer, mask)
				: Physics.OverlapSphereNonAlloc(center, range, _overlapBuffer);

			if (hitCount >= _overlapBuffer.Length)
			{
				_step = "GrowOverlapBuffer";
				Collider[] grown = new Collider[_overlapBuffer.Length * 2];
				Array.Copy(_overlapBuffer, grown, _overlapBuffer.Length);
				_overlapBuffer = grown;
				hitCount = mask >= 0
					? Physics.OverlapSphereNonAlloc(center, range, _overlapBuffer, mask)
					: Physics.OverlapSphereNonAlloc(center, range, _overlapBuffer);
			}

			_passSeq++;
			int dropsSeen = 0;
			int allowPass = 0;
			int blocked = 0;
			int capAllow = 0;
			int capSuppress = 0;
			int capSkip = 0;
			int alreadyOff = 0;
			int errors = 0;
			bool interesting = false;
			object inventoryObj = null;
			bool inventoryTried = false;
			string invHow = "not-needed";

			for (int i = 0; i < hitCount; i++)
			{
				_step = "Loop.collider";
				_item = $"hit[{i}/{hitCount}]";
				Collider col = _overlapBuffer[i];
				if ((Object)(object)col == (Object)null)
				{
					continue;
				}

				string colName = "?";
				try
				{
					_step = "Loop.colliderName";
					colName = ((Object)col.gameObject).name;
					_item = colName;
				}
				catch (Exception ex)
				{
					errors++;
					Fail("Loop.colliderName", ex);
					continue;
				}

				_step = "Loop.GetItemDrop";
				_item = colName;
				ItemDrop drop = TryGetItemDrop(col, colName, ref errors);
				if ((Object)(object)drop == (Object)null)
				{
					continue;
				}

				dropsSeen++;

				string rawName = colName;
				try
				{
					_step = "Loop.dropGameObjectName";
					rawName = ((Object)((Component)drop).gameObject).name;
					_item = rawName;
				}
				catch (Exception ex)
				{
					errors++;
					Fail("Loop.dropGameObjectName item=" + colName, ex);
					continue;
				}

				string prefab = PickupRuleStore.NormalizePrefabName(rawName);
				_item = prefab + " raw=" + rawName;

				bool auto;
				try
				{
					_step = "Loop.read_m_autoPickup";
					auto = drop.m_autoPickup;
				}
				catch (Exception ex)
				{
					errors++;
					Fail("Loop.read_m_autoPickup", ex);
					continue;
				}

				if (!auto)
				{
					alreadyOff++;
					continue;
				}

				PickupRule rule = PickupRuleStore.GetRule(prefab);

				if (rule.Mode == PickupRuleMode.Allow)
				{
					allowPass++;
					continue;
				}

				interesting = true;

				// Block never needs inventory — this is why eyes were still picking up.
				if (rule.Mode == PickupRuleMode.Block)
				{
					_step = "Block.suppress";
					_item = prefab;
					Suppress(drop, prefab);
					blocked++;
					continue;
				}

				if (!inventoryTried)
				{
					inventoryTried = true;
					inventoryObj = TryGetInventoryObject(__instance, out invHow);
				}

				_step = "Cap.enter";
				_item = prefab;
				if (inventoryObj == null)
				{
					// Can't count → suppress Cap piles (never overshoot).
					Log($"Cap SUPPRESS prefab='{prefab}' reason=no-inventory ({invHow})");
					Suppress(drop, prefab);
					capSuppress++;
					continue;
				}

				switch (ApplyCap(inventoryObj, drop, prefab, rawName, rule.Cap))
				{
					case CapDecision.Allow:
						capAllow++;
						break;
					case CapDecision.Suppress:
						capSuppress++;
						break;
					default:
						capSkip++;
						break;
				}
			}

			_step = "Summary";
			_item = "-";
			// Only periodic summary while debug is on — never per-hit spam (loot piles).
			if (SonicPickupFiltersPlugin.DebugEnabled &&
			    Time.unscaledTime >= _nextSummaryLog &&
			    (interesting || dropsSeen > 0))
			{
				_nextSummaryLog = Time.unscaledTime + 2f;
				Log(
					$"SUMMARY hits={hitCount} drops={dropsSeen} allow={allowPass} block={blocked} " +
					$"capAllow={capAllow} capSuppress={capSuppress} capSkip={capSkip} " +
					$"alreadyOff={alreadyOff} errors={errors} suppressed={Suppressed.Count} " +
					$"inv={invHow} rules={PickupRuleStore.FormatRulesSnapshot()}");
			}
		}

		private static string FormatRule(PickupRule rule)
		{
			if (rule.Mode == PickupRuleMode.Block)
			{
				return "Block";
			}

			if (rule.Mode == PickupRuleMode.Cap)
			{
				return "Cap:" + rule.Cap;
			}

			return "Allow";
		}

		/// <summary>
		/// Runtime reflection only — typed FieldInfo/GetInventory against compile-time Inventory
		/// throws InvalidCastException under ScriptEngine (fail# spam at step=GetInventory).
		/// </summary>
		private static object TryGetInventoryObject(object player, out string how)
		{
			if (player == null)
			{
				how = "player-null";
				return null;
			}

			try
			{
				_step = "inv.RuntimeGetInventory";
				Type pt = player.GetType();
				MethodInfo mi = pt.GetMethod(
					"GetInventory",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null,
					Type.EmptyTypes,
					null);
				if (mi != null)
				{
					object result = mi.Invoke(player, null);
					how = "RuntimeGetInventory";
					return result;
				}

				how = "GetInventory-missing";
			}
			catch (TargetInvocationException tex)
			{
				how = "GetInventory-threw";
				Fail("inv.RuntimeGetInventory", tex.InnerException ?? tex);
			}
			catch (Exception ex)
			{
				how = "GetInventory-threw";
				Fail("inv.RuntimeGetInventory", ex);
			}

			try
			{
				_step = "inv.RuntimeField";
				Type t = player.GetType();
				while (t != null)
				{
					FieldInfo fi = t.GetField(
						"m_inventory",
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
					if (fi != null)
					{
						object raw = fi.GetValue(player);
						how = "RuntimeField:" + t.Name + " type=" + (raw == null ? "null" : raw.GetType().FullName);
						return raw;
					}

					t = t.BaseType;
				}

				how = (how ?? "") + "+no-m_inventory-field";
			}
			catch (Exception ex)
			{
				Fail("inv.RuntimeField", ex);
				how = (how ?? "") + "+field-threw";
			}

			return null;
		}

		private static int ResolveMask(Player player)
		{
			if (AutoPickupMaskField == null)
			{
				return -1;
			}

			try
			{
				object maskValue = AutoPickupMaskField.GetValue(player);
				if (maskValue is int maskInt)
				{
					return maskInt;
				}

				Log("m_autoPickupMask unexpected type=" +
				    (maskValue == null ? "null" : maskValue.GetType().FullName));
			}
			catch (Exception ex)
			{
				Fail("ResolveMask", ex);
			}

			return -1;
		}

		private static ItemDrop TryGetItemDrop(Collider col, string colName, ref int errors)
		{
			try
			{
				_step = "GetComponent<ItemDrop>";
				_item = colName;
				ItemDrop drop = col.GetComponent<ItemDrop>();
				if ((Object)(object)drop != (Object)null)
				{
					return drop;
				}

				_step = "attachedRigidbody";
				Rigidbody rb = col.attachedRigidbody;
				if ((Object)(object)rb == (Object)null)
				{
					return null;
				}

				_step = "rb.GetComponent<ItemDrop>";
				drop = rb.GetComponent<ItemDrop>();
				if ((Object)(object)drop != (Object)null)
				{
					return drop;
				}

				_step = "rb.GetComponentInParent<ItemDrop>";
				return rb.GetComponentInParent<ItemDrop>();
			}
			catch (Exception ex)
			{
				errors++;
				Fail("TryGetItemDrop", ex);
				return null;
			}
		}

		private enum CapDecision
		{
			Allow,
			Suppress,
			Skip
		}

		private static CapDecision ApplyCap(object inventory, ItemDrop drop, string prefab, string rawName, int cap)
		{
			_step = "Cap.TryShared";
			_item = prefab;
			string shared = TryShared(drop, prefab);

			_step = "Cap.TryStack";
			int stack = TryStack(drop, prefab);

			_step = "Cap.TryMaxStack";
			int maxStack = TryMaxStack(drop, prefab);

			if (string.IsNullOrEmpty(shared) || stack < 1)
			{
				Log($"Cap SKIP prefab='{prefab}' raw='{rawName}' shared='{shared}' stack={stack} maxStack={maxStack}");
				return CapDecision.Skip;
			}

			_step = "Cap.CountHave";
			_item = prefab + " shared=" + shared;
			int have = CountHaveReflect(inventory, shared, prefab, out string countVia);
			PendingPickup.TryGetValue(shared, out int pending);
			int remaining = cap - have - pending;

			if (remaining <= 0)
			{
				Suppress(drop, prefab);
				return CapDecision.Suppress;
			}

			int take = stack;
			if (stack > remaining)
			{
				if (!TryCapShrink(drop, prefab, remaining, stack - remaining))
				{
					Log($"Cap SUPPRESS prefab='{prefab}' reason=stack({stack})>rem({remaining}) shrink-failed");
					Suppress(drop, prefab);
					return CapDecision.Suppress;
				}

				take = remaining;
				Log($"Cap SHRINK prefab='{prefab}' take={take} leftOnGround={stack - remaining}");
			}

			PendingPickup[shared] = pending + take;
			Log($"Cap ALLOW prefab='{prefab}' take={take} have={have} via={countVia}");
			return CapDecision.Allow;
		}

		/// <summary>
		/// Shrink ground pile to <paramref name="take"/> and spawn <paramref name="leave"/> via ObjectDB
		/// Instantiate (avoids ItemData.Clone / DropItem InvalidCast).
		/// </summary>
		private static bool TryCapShrink(ItemDrop drop, string prefab, int take, int leave)
		{
			if (take < 1)
			{
				return false;
			}

			try
			{
				_step = "Cap.SetStack";
				if (drop.m_itemData != null)
				{
					drop.m_itemData.m_stack = take;
				}

				drop.SetStack(take);
			}
			catch (Exception ex)
			{
				Fail("Cap.SetStack", ex);
				return false;
			}

			if (leave < 1)
			{
				return true;
			}

			if ((Object)(object)ObjectDB.instance == (Object)null)
			{
				Log("Cap shrink: ObjectDB null — took " + take + ", lost leave " + leave);
				return true;
			}

			try
			{
				_step = "Cap.InstantiateLeftover";
				GameObject template = ObjectDB.instance.GetItemPrefab(prefab);
				if ((Object)(object)template == (Object)null)
				{
					Log("Cap shrink: no prefab '" + prefab + "' — took " + take + ", lost leave " + leave);
					return true;
				}

				Vector3 pos = ((Component)drop).transform.position + Vector3.up * 0.05f;
				Quaternion rot = ((Component)drop).transform.rotation;
				GameObject spawned = Object.Instantiate(template, pos, rot);
				ItemDrop leftover = spawned.GetComponent<ItemDrop>();
				if ((Object)(object)leftover != (Object)null)
				{
					if (leftover.m_itemData != null)
					{
						leftover.m_itemData.m_stack = leave;
					}

					try
					{
						leftover.SetStack(leave);
					}
					catch (Exception ex)
					{
						Fail("Cap.leftover.SetStack", ex);
					}

					leftover.m_autoPickup = false;
					// Next Prefix will Cap-evaluate the leftover; restore so it can be considered.
					leftover.m_autoPickup = true;
				}

				return true;
			}
			catch (Exception ex)
			{
				Fail("Cap.InstantiateLeftover", ex);
				// Still ok to pick the shrunk pile — leftover may be lost.
				return true;
			}
		}

		private static int CountHaveReflect(object inventory, string shared, string prefab, out string method)
		{
			if (inventory == null)
			{
				method = "inv-null";
				return 0;
			}

			try
			{
				_step = "Cap.CountItems.Invoke";
				_item = prefab + " shared=" + shared;
				MethodInfo mi = inventory.GetType().GetMethod(
					"CountItems",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
					null,
					new[] { typeof(string), typeof(int), typeof(bool) },
					null);
				if (mi != null)
				{
					object n = mi.Invoke(inventory, new object[] { shared, -1, true });
					method = "CountItems";
					return Convert.ToInt32(n);
				}

				method = "CountItems-missing";
			}
			catch (TargetInvocationException tex)
			{
				Fail("Cap.CountItems.Invoke", tex.InnerException ?? tex);
				method = "CountItems-threw";
			}
			catch (Exception ex)
			{
				Fail("Cap.CountItems.Invoke", ex);
				method = "CountItems-threw";
			}

			// Fallback only when CountItems is unavailable — full inventory scan is expensive in loot piles.
			try
			{
				_step = "Cap.GetAllItems.Scan";
				int scanned = CountHaveByScan(inventory, shared);
				if (scanned >= 0)
				{
					method = "scan";
					return scanned;
				}
			}
			catch (Exception ex)
			{
				Fail("Cap.GetAllItems.Scan", ex);
				method = "scan-threw";
			}

			return 0;
		}

		/// <summary>
		/// Sum stacks via GetAllItems() reflection — avoids typed List&lt;ItemData&gt; across ScriptEngine assemblies.
		/// </summary>
		private static int CountHaveByScan(object inventory, string shared)
		{
			MethodInfo getAll = inventory.GetType().GetMethod(
				"GetAllItems",
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
				null,
				Type.EmptyTypes,
				null);
			if (getAll == null)
			{
				return -1;
			}

			object listObj = getAll.Invoke(inventory, null);
			if (listObj == null)
			{
				return 0;
			}

			if (!(listObj is IEnumerable enumerable))
			{
				return -1;
			}

			int sum = 0;
			foreach (object item in enumerable)
			{
				if (item == null)
				{
					continue;
				}

				Type itemType = item.GetType();
				FieldInfo sharedField = itemType.GetField(
					"m_shared",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				FieldInfo stackField = itemType.GetField(
					"m_stack",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (sharedField == null || stackField == null)
				{
					continue;
				}

				object sharedObj = sharedField.GetValue(item);
				if (sharedObj == null)
				{
					continue;
				}

				FieldInfo nameField = sharedObj.GetType().GetField(
					"m_name",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (nameField == null)
				{
					continue;
				}

				string name = nameField.GetValue(sharedObj) as string;
				if (!string.Equals(name, shared, StringComparison.Ordinal))
				{
					continue;
				}

				sum += Math.Max(0, Convert.ToInt32(stackField.GetValue(item)));
			}

			return sum;
		}

		private static string TryShared(ItemDrop drop, string prefab)
		{
			try
			{
				_step = "Read.m_itemData";
				_item = prefab;
				ItemDrop.ItemData data = drop.m_itemData;
				if (data == null)
				{
					return string.Empty;
				}

				_step = "Read.m_shared";
				ItemDrop.ItemData.SharedData shared = data.m_shared;
				if (shared == null)
				{
					return string.Empty;
				}

				_step = "Read.m_shared.m_name";
				return shared.m_name ?? string.Empty;
			}
			catch (Exception ex)
			{
				Fail("TryShared", ex);
				return string.Empty;
			}
		}

		private static int TryStack(ItemDrop drop, string prefab)
		{
			try
			{
				_step = "Read.m_stack";
				_item = prefab;
				ItemDrop.ItemData data = drop.m_itemData;
				return data != null ? Math.Max(1, data.m_stack) : -1;
			}
			catch (Exception ex)
			{
				Fail("TryStack", ex);
				return -1;
			}
		}

		private static int TryMaxStack(ItemDrop drop, string prefab)
		{
			try
			{
				_step = "Read.m_maxStackSize";
				_item = prefab;
				ItemDrop.ItemData data = drop.m_itemData;
				return data?.m_shared != null ? data.m_shared.m_maxStackSize : -1;
			}
			catch (Exception ex)
			{
				Fail("TryMaxStack", ex);
				return -1;
			}
		}

		private static void Suppress(ItemDrop drop, string prefab)
		{
			try
			{
				_step = "Suppress.set_m_autoPickup_false";
				_item = prefab;
				drop.m_autoPickup = false;
				Suppressed.Add(drop);
			}
			catch (Exception ex)
			{
				Fail("Suppress", ex);
			}
		}

		[HarmonyPostfix]
		private static void Postfix(Player __instance, float dt)
		{
			_step = "Postfix";
			_item = "suppressed=" + Suppressed.Count;
			try
			{
				if (SonicPickupFiltersPlugin.DebugEnabled && Suppressed.Count > 0)
				{
					Log("Postfix restore count=" + Suppressed.Count);
				}

				RestoreSuppressed();
			}
			catch (Exception ex)
			{
				Fail("Postfix", ex);
				Suppressed.Clear();
			}

			PendingPickup.Clear();
			_step = "Postfix.done";
		}

		private static void RestoreSuppressed()
		{
			for (int i = 0; i < Suppressed.Count; i++)
			{
				_step = "RestoreSuppressed[" + i + "]";
				ItemDrop drop = Suppressed[i];
				if ((Object)(object)drop == (Object)null)
				{
					continue;
				}

				try
				{
					_item = ((Object)((Component)drop).gameObject).name;
				}
				catch
				{
					_item = "drop#" + i;
				}

				try
				{
					drop.m_autoPickup = true;
				}
				catch (Exception ex)
				{
					Fail("RestoreSuppressed.set_true", ex);
				}
			}

			Suppressed.Clear();
		}

		private static void Log(string message)
		{
			SonicPickupFiltersPlugin.DebugLog("#" + _passSeq + " [" + _step + "] " + message);
		}

		private static void Fail(string where, Exception ex)
		{
			_failLogs++;
			// Rate-limit: first 8, then at most once per 2s (FixedUpdate would flood the log).
			float now = Time.unscaledTime;
			if (_failLogs > 8 && now < _nextFailLog)
			{
				return;
			}

			_nextFailLog = now + 2f;
			SonicPickupFiltersPlugin.ModLogger?.LogWarning(
				$"#{_passSeq} FAIL where={where} step={_step} item={_item} " +
				$"ex={ex.GetType().Name}: {ex.Message} (fail#{_failLogs})");
		}
	}
}
