using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace SonicPickupFilters
{
	[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
	[BepInDependency(Jotunn.Main.ModGuid)]
	[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
	public sealed class SonicPickupFiltersPlugin : BaseUnityPlugin
	{
		public const string PluginGuid = "com.sonicdm.valheim.sonicpickupfilters";

		public const string PluginName = "Sonic Pickup Filters";

		public const string PluginVersion = "0.1.0";

		internal const string HotkeyButtonName = "SonicPickupFilters_Toggle";

		internal static ManualLogSource ModLogger;

		internal static ConfigEntry<bool> Enabled;

		internal static ConfigEntry<bool> DebugLogging;

		internal static ConfigEntry<bool> DefaultCapUsesMaxStack;

		internal static ConfigEntry<string> Rules;

		internal static ConfigEntry<KeyCode> ToggleKey;

		internal static ConfigEntry<KeyboardShortcut> InventoryBlockModifier;

		internal static bool DebugEnabled => DebugLogging != null && DebugLogging.Value;

		private Harmony _harmony;

		private ButtonConfig _toggleButton;

		private bool _subscribedGui;

		private void Awake()
		{
			ModLogger = Logger;

			Enabled = Config.Bind("General", "Enabled", true, "Enable Sonic Pickup Filters.");
			DebugLogging = Config.Bind(
				"General",
				"DebugLogging",
				false,
				"Write diagnostic [DEBUG] lines to BepInEx LogOutput.log. Leave off in normal play — spam in loot piles is very expensive.");

			// Earlier 0.1.x builds forced DebugLogging=true into the persisted cfg.
			ConfigEntry<bool> debugMigrated = Config.Bind(
				"General",
				"DebugLoggingMigratedOff",
				false,
				"Internal: one-shot clear of forced DebugLogging from early 0.1.x builds.");
			if (!debugMigrated.Value)
			{
				DebugLogging.Value = false;
				debugMigrated.Value = true;
			}

			DefaultCapUsesMaxStack = Config.Bind(
				"Rules",
				"DefaultCapUsesMaxStack",
				true,
				"When entering Cap mode, default the max to the item's max stack size (one full stack).");
			Rules = Config.Bind(
				"Rules",
				"ItemRules",
				"",
				"Comma-separated prefab rules: Prefab:0 = Block, Prefab:N = Cap at N. Missing = Allow. Example: BlackMetalScrap:0,Wood:50");
			ToggleKey = Config.Bind(
				"UI",
				"ToggleKey",
				KeyCode.L,
				"Hotkey to open/close the Pickup Filter panel.");
			InventoryBlockModifier = Config.Bind(
				"UI",
				"InventoryBlockModifier",
				new KeyboardShortcut(KeyCode.LeftAlt),
				"Hold this modifier and right-click an item in your inventory to toggle Block (never auto-pick).");

			ModLocalization.Register();
			PickupRuleStore.Init(Rules);

			Rules.SettingChanged += (_, __) => PickupRuleStore.ReloadFromConfig();

			Logger.LogInfo(
				PluginName + " " + PluginVersion + " Awake. DebugLogging=" + DebugLogging.Value +
				" ItemRules='" + (Rules.Value ?? string.Empty) + "'");

			// Drop any leftover panel if the plugin is soft-reloaded mid-session.
			PickupFilterPanel.DestroyPanel();

			if (GUIManager.IsHeadless())
			{
				Logger.LogInfo(PluginName + " " + PluginVersion + " skipped GUI/patches on headless server.");
				return;
			}

			_toggleButton = new ButtonConfig
			{
				Name = HotkeyButtonName,
				Key = ToggleKey != null ? ToggleKey.Value : KeyCode.L,
				ActiveInGUI = true,
				ActiveInCustomGUI = true
			};
			InputManager.Instance.AddButton(PluginGuid, _toggleButton);

			GUIManager.OnCustomGUIAvailable += OnCustomGuiAvailable;
			_subscribedGui = true;

			_harmony = new Harmony(PluginGuid);
			try
			{
				_harmony.PatchAll();
				Logger.LogInfo(PluginName + " " + PluginVersion + " loaded (Harmony ok).");
				DebugLog("DebugLog path OK — open panel / cycle category to see more [DEBUG] lines.");
			}
			catch (Exception ex)
			{
				try
				{
					_harmony.UnpatchSelf();
				}
				catch
				{
				}

				Enabled.Value = false;
				Logger.LogError(PluginName + " failed to apply Harmony patches and has been disabled.");
				Logger.LogError(ex);
			}

			TryRegisterCommands();

			// ScriptEngine mid-session reload: CustomGUI + InventoryGui already exist — event/Awake won't re-fire.
			TryCatchUpUi();
		}

		private void TryCatchUpUi()
		{
			try
			{
				if (GUIManager.Instance != null && GUIManager.CustomGUIFront != null)
				{
					OnCustomGuiAvailable();
				}

				InventoryBarButton.TryInjectExisting();
			}
			catch (Exception ex)
			{
				Logger.LogWarning("TryCatchUpUi failed: " + ex.Message);
			}
		}

		private void OnDestroy()
		{
			// Required for BepInEx ScriptEngine F6 reload — must unpatch + drop UI or hooks stack.
			if (_subscribedGui)
			{
				GUIManager.OnCustomGUIAvailable -= OnCustomGuiAvailable;
				_subscribedGui = false;
			}

			try
			{
				AutoPickupPatches.ResetRuntimeState();
			}
			catch
			{
			}

			PickupFilterPanel.DestroyPanel();
			InventoryBarButton.DestroyInjected();

			if (_harmony != null)
			{
				try
				{
					_harmony.UnpatchSelf();
				}
				catch (Exception ex)
				{
					Logger.LogWarning("UnpatchSelf on destroy: " + ex.Message);
				}

				_harmony = null;
			}

			Logger.LogInfo(PluginName + " unloaded (ScriptEngine-safe).");
		}

		private static bool _commandRegistered;

		private void TryRegisterCommands()
		{
			if (_commandRegistered)
			{
				return;
			}

			try
			{
				new Terminal.ConsoleCommand(
					"spf_dump",
					"Sonic Pickup Filters: dump Cap/Block rules + nearby auto-pickup candidates to console and log",
					args =>
					{
						string dump = AutoPickupPatches.DumpNearby(Player.m_localPlayer);
						foreach (string line in dump.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
						{
							args.Context.AddString(line);
						}

						ModLogger?.LogInfo(dump);
					},
					false, false, false, false, false, false, null, false, false, true);

				_commandRegistered = true;
				Logger.LogInfo("Registered command: spf_dump");
			}
			catch (Exception ex)
			{
				Logger.LogWarning("spf_dump register failed: " + ex.Message);
			}
		}

		private void OnCustomGuiAvailable()
		{
			PickupFilterPanel.OnGuiReady();
		}

		private void Update()
		{
			if (!IsModEnabled() || GUIManager.IsHeadless())
			{
				return;
			}

			PickupFilterPanel.ProcessQueuedRefresh();

			if (ZInput.instance != null && ZInput.GetButtonDown(HotkeyButtonName))
			{
				PickupFilterPanel.TogglePanel();
			}
		}

		internal static bool IsModEnabled()
		{
			return Enabled == null || Enabled.Value;
		}

		internal static void DebugLog(string message)
		{
			if (DebugEnabled && ModLogger != null)
			{
				// LogInfo so it shows with default BepInEx log level (LogDebug is often filtered).
				ModLogger.LogInfo("[DEBUG] " + message);
			}
		}
	}
}
