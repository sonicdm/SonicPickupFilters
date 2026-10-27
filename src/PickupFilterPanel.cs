using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Jotunn.GUI;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SonicPickupFilters
{
	internal static class PickupFilterPanel
	{
		private enum ViewTab
		{
			All,
			Blocked,
			Capped
		}

		private const float PanelW = 980f;

		private const float PanelH = 780f;

		private const float RowHeight = 36f;

		// Row tall enough for a Jötunn CreateToggle (docs use 40×40; we use 28 in a 36px row).
		private const float ItemRowHeight = 36f;

		private const float CategoryHeaderHeight = 22f;

		private const float ScrollH = 500f;

		private const float RowInnerW = 880f;

		private const float IconSize = 28f;

		private const float ToggleSize = 28f;

		private const float CapFieldW = 100f;

		private const float CapFieldH = 28f;

		// Absolute column X (left-anchored) within each row — no LayoutGroup guessing.
		// Layout: [icon][name........][Block][Cap][Qty]
		private const float ColIconX = 8f;

		private const float ColNameX = 44f;

		private const float ColNameW = 480f;

		private const float ColBlockX = 540f;

		private const float ColCapToggleX = 620f;

		private const float ColCapQtyX = 690f;

		private static readonly FieldInfo KnownMaterialField =
			AccessTools.Field(typeof(Player), "m_knownMaterial");

		private static readonly Dictionary<string, Sprite> IconCache = new Dictionary<string, Sprite>();

		private static readonly Color ColNormal = Color.white;

		private static readonly Color ColBlocked = new Color(1f, 0.25f, 0.25f, 1f);

		private static readonly Color ColCapped = new Color(1f, 0.85f, 0.35f, 1f);

		private static GameObject _panel;

		private static RectTransform _contentRoot;

		private static ScrollRect _scrollRect;

		private static InputField _searchInput;

		private static Dropdown _categoryDropdown;

		private static Text _tabAllLabel;

		private static Text _tabBlockedLabel;

		private static Text _tabCappedLabel;

		private static Button _tabAllButton;

		private static Button _tabBlockedButton;

		private static Button _tabCappedButton;

		private static GameObject _capEditor;

		private static InputField _capInput;

		private static Text _capEditorTitle;

		private static string _capEditPrefab = string.Empty;

		private static int _capEditDefault = 1;

		private static bool _guiReady;

		private static string _searchText = string.Empty;

		private static int _categoryIndex;

		private static ViewTab _viewTab = ViewTab.All;

		private static List<ItemDrop.ItemData.ItemType?> _categoryOptions =
			new List<ItemDrop.ItemData.ItemType?> { null };

		private static List<string> _categoryLabels = new List<string>();

		private static bool _suppressCategoryCallback;

		private static bool _refreshQueued;

		private static int _refreshReadyFrame;

		private static float _contentWidth = 900f;

		/// <summary>When true, next RefreshContent jumps to top (open / search / tab / clear).</summary>
		private static bool _resetScrollOnRefresh = true;

		internal static bool IsOpen =>
			(Object)(object)_panel != (Object)null && _panel.activeSelf;

		internal static void OnGuiReady()
		{
			_guiReady = GUIManager.Instance != null && GUIManager.CustomGUIFront != null;
		}

		internal static void TogglePanel()
		{
			if (!_guiReady && GUIManager.Instance != null)
			{
				_guiReady = GUIManager.CustomGUIFront != null;
			}

			if (!_guiReady || GUIManager.IsHeadless())
			{
				return;
			}

			if ((Object)(object)_panel == (Object)null)
			{
				BuildPanel();
			}
			else if ((Object)(object)_contentRoot == (Object)null || (Object)(object)_scrollRect == (Object)null)
			{
				DestroyPanel();
				BuildPanel();
			}

			if ((Object)(object)_panel == (Object)null)
			{
				return;
			}

			bool open = !_panel.activeSelf;
			_panel.SetActive(open);
			GUIManager.BlockInput(open);
			SonicPickupFiltersPlugin.DebugLog("TogglePanel open=" + open);
			if (open)
			{
				_viewTab = ViewTab.All;
				EnsureCategoryOptions();
				_resetScrollOnRefresh = true;
				RefreshContent();
			}
			else
			{
				_refreshQueued = false;
				HideCapEditor();
			}
		}

		internal static void RefreshIfOpen()
		{
			if (IsOpen)
			{
				RefreshContent();
			}
		}

		/// <summary>
		/// Run deferred UI rebuilds after Dropdown finishes its click handler (ClearOptions /
		/// destroying list items mid-callback crashes Valheim).
		/// </summary>
		internal static void ProcessQueuedRefresh()
		{
			if (!_refreshQueued)
			{
				return;
			}

			// Wait until dropdown list teardown finishes (same-frame / +1 often hard-crashes).
			if (Time.frameCount < _refreshReadyFrame)
			{
				return;
			}

			_refreshQueued = false;
			if (IsOpen)
			{
				RefreshContent();
			}
		}

		internal static void DestroyPanel()
		{
			if ((Object)(object)_panel != (Object)null)
			{
				Object.Destroy(_panel);
			}

			_panel = null;
			_contentRoot = null;
			_scrollRect = null;
			_searchInput = null;
			_categoryDropdown = null;
			_tabAllLabel = null;
			_tabBlockedLabel = null;
			_tabCappedLabel = null;
			_tabAllButton = null;
			_tabBlockedButton = null;
			_tabCappedButton = null;
			_capEditor = null;
			_capInput = null;
			_capEditorTitle = null;
			_capEditPrefab = string.Empty;
			_viewTab = ViewTab.All;
			_searchText = string.Empty;
			_categoryIndex = 0;
			_categoryLabels.Clear();
			_suppressCategoryCallback = false;
			_refreshQueued = false;
			_refreshReadyFrame = 0;
			_tooltipTemplate = null;
		}

		private static void BuildPanel()
		{
			if (GUIManager.Instance == null || (Object)(object)GUIManager.CustomGUIFront == (Object)null)
			{
				SonicPickupFiltersPlugin.ModLogger?.LogError("GUIManager not ready for Pickup Filter panel.");
				return;
			}

			_panel = GUIManager.Instance.CreateWoodpanel(
				parent: GUIManager.CustomGUIFront.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: Vector2.zero,
				width: PanelW,
				height: PanelH,
				draggable: true);
			_panel.SetActive(false);

			// Title
			GUIManager.Instance.CreateText(
				text: ModLocalization.Translate(ModLocalization.Title, "Pickup Filter"),
				parent: _panel.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -36f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 28,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: 500f,
				height: 36f,
				addContentSizeFitter: false);

			// Hint — explains Block + Cap relationship
			GameObject hintGo = GUIManager.Instance.CreateText(
				text: ModLocalization.Translate(
					ModLocalization.Hint,
					"Block = never auto-pick. Cap = pick until the count (defaults to max stack — edit the number anytime)."),
				parent: _panel.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -78f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 12,
				color: Color.white,
				outline: true,
				outlineColor: Color.black,
				width: 900f,
				height: 40f,
				addContentSizeFitter: false);
			Text hintText = hintGo.GetComponent<Text>();
			if ((Object)(object)hintText != (Object)null)
			{
				hintText.alignment = TextAnchor.UpperCenter;
				hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
				hintText.verticalOverflow = VerticalWrapMode.Overflow;
			}

			// Tab row — HorizontalLayoutGroup so buttons cannot overlap
			RectTransform tabRow = CreateRow("TabRow", new Vector2(0f, -128f), 920f, RowHeight);
			_tabAllButton = AddRowButton(tabRow, "All", 140f, () => SetViewTab(ViewTab.All), out _tabAllLabel);
			_tabBlockedButton = AddRowButton(tabRow, "Blocked", 160f, () => SetViewTab(ViewTab.Blocked), out _tabBlockedLabel);
			_tabCappedButton = AddRowButton(tabRow, "Capped", 160f, () => SetViewTab(ViewTab.Capped), out _tabCappedLabel);
			AttachTooltip(
				_tabBlockedButton.gameObject,
				ModLocalization.Translate(ModLocalization.TabBlocked, "Blocked"),
				"Items with Block on and no Cap (never auto-picked).");
			AttachTooltip(
				_tabCappedButton.gameObject,
				ModLocalization.Translate(ModLocalization.TabCapped, "Capped"),
				"Items with Block on and a Cap number (auto-pick until that count).");

			// Filter row
			RectTransform filterRow = CreateRow("FilterRow", new Vector2(0f, -176f), 920f, RowHeight);

			GameObject searchGo = GUIManager.Instance.CreateInputField(
				parent: filterRow,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: Vector2.zero,
				contentType: InputField.ContentType.Standard,
				placeholderText: ModLocalization.Translate(ModLocalization.SearchPlaceholder, "Search items…"),
				fontSize: 14,
				width: 360f,
				height: RowHeight);
			PrepareLayoutChild(searchGo, 360f, RowHeight);
			_searchInput = searchGo.GetComponent<InputField>();
			if ((Object)(object)_searchInput != (Object)null)
			{
				GUIManager.Instance.ApplyInputFieldStyle(_searchInput, 14);
				_searchInput.onValueChanged.AddListener(OnSearchChanged);
			}

			GameObject clearGo = GUIManager.Instance.CreateButton(
				text: ModLocalization.Translate(ModLocalization.Clear, "Clear"),
				parent: filterRow,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: Vector2.zero,
				width: 100f,
				height: RowHeight);
			PrepareLayoutChild(clearGo, 100f, RowHeight);
			Button clearButton = clearGo.GetComponent<Button>();
			GUIManager.Instance.ApplyButtonStyle(clearButton, 14);
			clearButton.onClick.AddListener(ClearFilters);

			// Real dropdown. Never Hide()/ClearOptions inside onValueChanged — defer list rebuild 2 frames.
			GameObject dropdownGo = GUIManager.Instance.CreateDropDown(
				parent: filterRow,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: Vector2.zero,
				fontSize: 14,
				width: 280f,
				height: RowHeight);
			PrepareLayoutChild(dropdownGo, 280f, RowHeight);
			_categoryDropdown = dropdownGo.GetComponent<Dropdown>();
			if ((Object)(object)_categoryDropdown != (Object)null)
			{
				// CreateDropDown sets itemText height = dropdown height (36), which overflows into
				// the next option and makes clicks select the row below. Fix template item hits.
				FixDropdownOptionHits(_categoryDropdown, 26f);
				_categoryDropdown.onValueChanged.AddListener(OnCategoryChanged);
			}

			// Scroll view for items — CreateScrollView returns a wrapper Canvas; fix its
			// upper-right Scroll View child so content actually fills the visible area.
			GameObject scrollGo = GUIManager.Instance.CreateScrollView(
				parent: _panel.transform,
				showHorizontalScrollbar: false,
				showVerticalScrollbar: true,
				handleSize: 8f,
				handleDistanceToBorder: 2f,
				handleColors: GUIManager.Instance.ValheimScrollbarHandleColorBlock,
				slidingAreaBackgroundColor: GUIManager.Instance.ValheimBeige,
				width: 920f,
				height: ScrollH);
			scrollGo.name = "PickupFilterScroll";
			RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
			scrollRt.anchorMin = new Vector2(0.5f, 1f);
			scrollRt.anchorMax = new Vector2(0.5f, 1f);
			scrollRt.pivot = new Vector2(0.5f, 1f);
			scrollRt.anchoredPosition = new Vector2(0f, -228f);
			scrollRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 920f);
			scrollRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ScrollH);

			FixJotunnScrollFill(scrollGo, 920f, ScrollH);

			_scrollRect = scrollGo.GetComponentInChildren<ScrollRect>(true);
			if ((Object)(object)_scrollRect == (Object)null)
			{
				SonicPickupFiltersPlugin.ModLogger?.LogError("ScrollRect missing.");
				return;
			}

			_scrollRect.horizontal = false;
			_scrollRect.vertical = true;
			_scrollRect.movementType = ScrollRect.MovementType.Clamped;
			_scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
			GUIManager.Instance.ApplyScrollRectStyle(_scrollRect);
			// ApplyScrollRectStyle may reset sensitivity — set after.
			_scrollRect.scrollSensitivity = 450f;
			_scrollRect.inertia = true;
			_scrollRect.decelerationRate = 0.135f;

			if ((Object)(object)_scrollRect.viewport != (Object)null)
			{
				Image vpImage = _scrollRect.viewport.GetComponent<Image>() ??
				                _scrollRect.viewport.gameObject.AddComponent<Image>();
				vpImage.color = new Color(0f, 0f, 0f, 0.01f);
				vpImage.raycastTarget = true;
			}

			_contentRoot = _scrollRect.content;
			_contentWidth = 920f - 2f * 2f - 8f;
			ConfigureContentRoot(_contentRoot, _contentWidth);

			// Close
			GameObject closeGo = GUIManager.Instance.CreateButton(
				text: ModLocalization.Translate(ModLocalization.Close, "Close"),
				parent: _panel.transform,
				anchorMin: new Vector2(0.5f, 0f),
				anchorMax: new Vector2(0.5f, 0f),
				position: new Vector2(0f, 40f),
				width: 200f,
				height: 48f);
			Button closeButton = closeGo.GetComponent<Button>();
			GUIManager.Instance.ApplyButtonStyle(closeButton);
			closeButton.onClick.AddListener(() =>
			{
				if (IsOpen)
				{
					TogglePanel();
				}
			});
		}

		private static RectTransform CreateRow(string name, Vector2 anchoredPos, float width, float height)
		{
			GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
			row.transform.SetParent(_panel.transform, false);
			RectTransform rt = row.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0.5f, 1f);
			rt.anchorMax = new Vector2(0.5f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.anchoredPosition = anchoredPos;
			rt.sizeDelta = new Vector2(width, height);

			HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
			layout.spacing = 10f;
			layout.padding = new RectOffset(0, 0, 0, 0);
			layout.childAlignment = TextAnchor.MiddleLeft;
			layout.childControlWidth = false;
			layout.childControlHeight = false;
			layout.childForceExpandWidth = false;
			layout.childForceExpandHeight = false;
			return rt;
		}

		private static Button AddRowButton(
			RectTransform row,
			string label,
			float width,
			UnityEngine.Events.UnityAction onClick,
			out Text labelText)
		{
			GameObject go = GUIManager.Instance.CreateButton(
				text: label,
				parent: row,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: Vector2.zero,
				width: width,
				height: RowHeight);
			PrepareLayoutChild(go, width, RowHeight);
			Button button = go.GetComponent<Button>();
			GUIManager.Instance.ApplyButtonStyle(button, 14);
			button.onClick.AddListener(onClick);
			labelText = go.GetComponentInChildren<Text>();
			return button;
		}

		private static void PrepareLayoutChild(GameObject go, float width, float height)
		{
			RectTransform rt = go.GetComponent<RectTransform>();
			if ((Object)(object)rt != (Object)null)
			{
				rt.anchorMin = new Vector2(0.5f, 0.5f);
				rt.anchorMax = new Vector2(0.5f, 0.5f);
				rt.pivot = new Vector2(0.5f, 0.5f);
				rt.sizeDelta = new Vector2(width, height);
				rt.anchoredPosition = Vector2.zero;
				rt.localScale = Vector3.one;
			}

			LayoutElement layoutElement = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
			layoutElement.preferredWidth = width;
			layoutElement.preferredHeight = height;
			layoutElement.minWidth = width;
			layoutElement.minHeight = height;
			layoutElement.flexibleWidth = 0f;
			layoutElement.flexibleHeight = 0f;
		}

		/// <summary>
		/// Jötunn CreateScrollView pins the inner "Scroll View" to upper-right, which clips
		/// content when the wrapper Canvas is repositioned. Stretch it to fill the wrapper.
		/// </summary>
		private static void FixJotunnScrollFill(GameObject scrollRoot, float width, float height)
		{
			Transform scrollViewTf = scrollRoot.transform.Find("Scroll View");
			if ((Object)(object)scrollViewTf == (Object)null)
			{
				return;
			}

			RectTransform scrollViewRt = scrollViewTf.GetComponent<RectTransform>();
			scrollViewRt.anchorMin = Vector2.zero;
			scrollViewRt.anchorMax = Vector2.one;
			scrollViewRt.pivot = new Vector2(0.5f, 0.5f);
			scrollViewRt.offsetMin = Vector2.zero;
			scrollViewRt.offsetMax = Vector2.zero;
			scrollViewRt.anchoredPosition = Vector2.zero;
			scrollViewRt.localScale = Vector3.one;

			Transform viewportTf = scrollViewTf.Find("Viewport");
			if ((Object)(object)viewportTf != (Object)null)
			{
				RectTransform viewportRt = viewportTf.GetComponent<RectTransform>();
				viewportRt.anchorMin = Vector2.zero;
				viewportRt.anchorMax = Vector2.one;
				viewportRt.pivot = new Vector2(0.5f, 0.5f);
				viewportRt.offsetMin = new Vector2(0f, 0f);
				viewportRt.offsetMax = new Vector2(-10f, 0f);
				viewportRt.anchoredPosition = Vector2.zero;
			}
		}

		private static void ConfigureContentRoot(RectTransform content, float width)
		{
			VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>() ??
			                          content.gameObject.AddComponent<VerticalLayoutGroup>();
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlHeight = true;
			vlg.childControlWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.spacing = 4f;
			vlg.padding = new RectOffset(8, 8, 8, 8);

			ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>() ??
			                          content.gameObject.AddComponent<ContentSizeFitter>();
			fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
			fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

			content.anchorMin = new Vector2(0f, 1f);
			content.anchorMax = new Vector2(0f, 1f);
			content.pivot = new Vector2(0f, 1f);
			content.anchoredPosition = Vector2.zero;
			content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
			content.localScale = Vector3.one;

			// Destroy GraphicRaycaster before Canvas (Unity forbids removing Canvas while GR depends on it).
			GraphicRaycaster nestedRaycaster = content.GetComponent<GraphicRaycaster>();
			if ((Object)(object)nestedRaycaster != (Object)null)
			{
				Object.DestroyImmediate(nestedRaycaster);
			}

			Canvas nested = content.GetComponent<Canvas>();
			if ((Object)(object)nested != (Object)null)
			{
				Object.DestroyImmediate(nested);
			}
		}

		private static void OnSearchChanged(string value)
		{
			_searchText = value ?? string.Empty;
			SonicPickupFiltersPlugin.DebugLog("Search='" + _searchText + "'");
			_resetScrollOnRefresh = true;
			RefreshContent();
		}

		private static void OnCategoryChanged(int index)
		{
			if (_suppressCategoryCallback)
			{
				return;
			}

			_categoryIndex = index;
			string label = index >= 0 && index < _categoryLabels.Count ? _categoryLabels[index] : "?";
			SonicPickupFiltersPlugin.DebugLog("Category dropdown → " + index + " (" + label + ")");
			// Do NOT call Hide()/ClearOptions here. Defer content rebuild past list teardown.
			_resetScrollOnRefresh = true;
			_refreshReadyFrame = Time.frameCount + 2;
			_refreshQueued = true;
		}

		private static void ClearFilters()
		{
			_searchText = string.Empty;
			_categoryIndex = 0;
			if ((Object)(object)_searchInput != (Object)null)
			{
				_searchInput.SetTextWithoutNotify(string.Empty);
			}

			SetCategoryDropdownValue(0);
			_resetScrollOnRefresh = true;
			RefreshContent();
		}

		private static void SetViewTab(ViewTab tab)
		{
			_viewTab = tab;
			// Blocked/Capped lists were empty because Ammo+search still applied. Reset category on tab change.
			_categoryIndex = 0;
			SetCategoryDropdownValue(0);
			if ((Object)(object)_searchInput != (Object)null && !string.IsNullOrEmpty(_searchText))
			{
				// Keep search, but log so empty results are explainable.
				SonicPickupFiltersPlugin.DebugLog("Tab " + tab + " with search still='" + _searchText + "'");
			}

			_resetScrollOnRefresh = true;
			RefreshContent();
		}

		private static void UpdateTabLabels()
		{
			SetTabLabel(_tabAllLabel, _tabAllButton, ViewTab.All,
				ModLocalization.Translate(ModLocalization.TabAll, "All"));
			SetTabLabel(_tabBlockedLabel, _tabBlockedButton, ViewTab.Blocked,
				ModLocalization.Translate(ModLocalization.TabBlocked, "Blocked") +
				" (" + PickupRuleStore.CountByMode(PickupRuleMode.Block) + ")");
			SetTabLabel(_tabCappedLabel, _tabCappedButton, ViewTab.Capped,
				ModLocalization.Translate(ModLocalization.TabCapped, "Capped") +
				" (" + PickupRuleStore.CountByMode(PickupRuleMode.Cap) + ")");
		}

		private static void SetTabLabel(Text label, Button button, ViewTab tab, string text)
		{
			if ((Object)(object)label != (Object)null)
			{
				label.text = text;
				label.color = _viewTab == tab ? GUIManager.Instance.ValheimOrange : Color.white;
			}

			if ((Object)(object)button != (Object)null)
			{
				ColorBlock colors = button.colors;
				colors.normalColor = _viewTab == tab
					? new Color(1f, 0.85f, 0.55f, 1f)
					: Color.white;
				button.colors = colors;
			}
		}

		private static void EnsureCategoryOptions()
		{
			List<ItemDrop.ItemData.ItemType> types = GetKnownItems()
				.Select(GetItemType)
				.Distinct()
				.OrderBy(t => ItemMatch.FriendlyItemTypeLabel(t))
				.ToList();

			_categoryOptions = new List<ItemDrop.ItemData.ItemType?> { null };
			_categoryOptions.AddRange(types.Select(t => (ItemDrop.ItemData.ItemType?)t));

			List<string> labels = new List<string>
			{
				ModLocalization.Translate(ModLocalization.CategoryAll, "All categories")
			};
			for (int i = 0; i < types.Count; i++)
			{
				labels.Add(ItemMatch.FriendlyItemTypeLabel(types[i]));
			}

			if (_categoryIndex < 0 || _categoryIndex >= _categoryOptions.Count)
			{
				_categoryIndex = 0;
			}

			bool same = labels.Count == _categoryLabels.Count;
			if (same)
			{
				for (int i = 0; i < labels.Count; i++)
				{
					if (!string.Equals(labels[i], _categoryLabels[i], StringComparison.Ordinal))
					{
						same = false;
						break;
					}
				}
			}

			if (same)
			{
				SetCategoryDropdownValue(_categoryIndex);
				return;
			}

			_categoryLabels = labels;
			if ((Object)(object)_categoryDropdown == (Object)null)
			{
				return;
			}

			_suppressCategoryCallback = true;
			try
			{
				_categoryDropdown.ClearOptions();
				_categoryDropdown.AddOptions(labels);
				_categoryDropdown.SetValueWithoutNotify(_categoryIndex);
			}
			finally
			{
				_suppressCategoryCallback = false;
			}

			SonicPickupFiltersPlugin.DebugLog("Category options rebuilt count=" + labels.Count);
		}

		private static void SetCategoryDropdownValue(int index)
		{
			if ((Object)(object)_categoryDropdown == (Object)null)
			{
				return;
			}

			_suppressCategoryCallback = true;
			try
			{
				_categoryDropdown.SetValueWithoutNotify(index);
			}
			finally
			{
				_suppressCategoryCallback = false;
			}
		}

		/// <summary>
		/// Jötunn CreateDropDown copies the control height onto itemText, so option labels are taller
		/// than the Item row and steal hits from the next option ("always picks the line below").
		/// </summary>
		private static void FixDropdownOptionHits(Dropdown dropdown, float optionHeight)
		{
			if ((Object)(object)dropdown == (Object)null)
			{
				return;
			}

			if ((Object)(object)dropdown.itemText != (Object)null)
			{
				RectTransform textRt = dropdown.itemText.rectTransform;
				textRt.anchorMin = new Vector2(0f, 0f);
				textRt.anchorMax = new Vector2(1f, 1f);
				textRt.offsetMin = new Vector2(10f, 2f);
				textRt.offsetMax = new Vector2(-10f, -2f);
				dropdown.itemText.raycastTarget = false;
				dropdown.itemText.alignment = TextAnchor.MiddleLeft;
			}

			if ((Object)(object)dropdown.template == (Object)null)
			{
				return;
			}

			RectTransform templateRt = dropdown.template;
			templateRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Min(280f, optionHeight * 8f));

			Transform content = dropdown.template.Find("Viewport/Content");
			if ((Object)(object)content != (Object)null)
			{
				VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>() ??
				                         content.gameObject.AddComponent<VerticalLayoutGroup>();
				vlg.childAlignment = TextAnchor.UpperLeft;
				vlg.childControlHeight = true;
				vlg.childControlWidth = true;
				vlg.childForceExpandHeight = false;
				vlg.childForceExpandWidth = true;
				vlg.spacing = 0f;
				vlg.padding = new RectOffset(0, 0, 0, 0);
			}

			Transform item = dropdown.template.Find("Viewport/Content/Item");
			if ((Object)(object)item == (Object)null)
			{
				return;
			}

			RectTransform itemRt = item as RectTransform;
			if ((Object)(object)itemRt != (Object)null)
			{
				itemRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, optionHeight);
			}

			LayoutElement le = item.GetComponent<LayoutElement>() ?? item.gameObject.AddComponent<LayoutElement>();
			le.minHeight = optionHeight;
			le.preferredHeight = optionHeight;
			le.flexibleHeight = 0f;

			// Background / toggle should fill the item and own the raycast.
			for (int i = 0; i < item.childCount; i++)
			{
				Transform child = item.GetChild(i);
				Image img = child.GetComponent<Image>();
				if ((Object)(object)img != (Object)null)
				{
					img.raycastTarget = true;
					RectTransform crt = child as RectTransform;
					if ((Object)(object)crt != (Object)null)
					{
						crt.anchorMin = Vector2.zero;
						crt.anchorMax = Vector2.one;
						crt.offsetMin = Vector2.zero;
						crt.offsetMax = Vector2.zero;
					}
				}

				Text txt = child.GetComponent<Text>();
				if ((Object)(object)txt != (Object)null)
				{
					txt.raycastTarget = false;
				}
			}

			Toggle toggle = item.GetComponent<Toggle>();
			if ((Object)(object)toggle != (Object)null)
			{
				toggle.toggleTransition = Toggle.ToggleTransition.None;
			}
		}

		private static void ClearContentChildren()
		{
			if ((Object)(object)_contentRoot == (Object)null)
			{
				return;
			}

			for (int i = _contentRoot.childCount - 1; i >= 0; i--)
			{
				Object.DestroyImmediate(_contentRoot.GetChild(i).gameObject);
			}
		}

		private static void RefreshContent()
		{
			if ((Object)(object)_contentRoot == (Object)null)
			{
				return;
			}

			bool resetScroll = _resetScrollOnRefresh;
			_resetScrollOnRefresh = false;
			float savedNorm = 1f;
			if (!resetScroll && (Object)(object)_scrollRect != (Object)null)
			{
				savedNorm = _scrollRect.verticalNormalizedPosition;
			}

			ClearContentChildren();

			List<ItemDrop> known = GetKnownItems();
			if (_viewTab == ViewTab.Blocked)
			{
				known = MergeRuleItems(known, PickupRuleMode.Block);
			}
			else if (_viewTab == ViewTab.Capped)
			{
				known = MergeRuleItems(known, PickupRuleMode.Cap);
			}

			// Do NOT rebuild dropdown options here — ClearOptions while/after a pick crashes.
			UpdateTabLabels();

			IEnumerable<ItemDrop> filtered = known.Where(d => ItemMatch.MatchesSearch(d, _searchText));

			// Category filter only on All tab — Blocked/Capped were empty with Ammo+wood still active.
			bool applyCategory = _viewTab == ViewTab.All &&
			                     _categoryIndex > 0 &&
			                     _categoryIndex < _categoryOptions.Count &&
			                     _categoryOptions[_categoryIndex].HasValue;
			if (applyCategory)
			{
				ItemDrop.ItemData.ItemType want = _categoryOptions[_categoryIndex].Value;
				filtered = filtered.Where(d => GetItemType(d) == want);
			}

			if (_viewTab == ViewTab.Blocked)
			{
				filtered = filtered.Where(d =>
					PickupRuleStore.GetRule(PickupRuleStore.NormalizePrefabName(((Object)d).name)).Mode ==
					PickupRuleMode.Block);
			}
			else if (_viewTab == ViewTab.Capped)
			{
				filtered = filtered.Where(d =>
					PickupRuleStore.GetRule(PickupRuleStore.NormalizePrefabName(((Object)d).name)).Mode ==
					PickupRuleMode.Cap);
			}

			List<IGrouping<ItemDrop.ItemData.ItemType, ItemDrop>> groups = filtered
				.GroupBy(GetItemType)
				.OrderBy(g => ItemMatch.FriendlyItemTypeLabel(g.Key))
				.ToList();

			int itemCount = groups.Sum(g => g
				.Select(d => PickupRuleStore.NormalizePrefabName(((Object)d).name))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Count());
			float t0 = Time.realtimeSinceStartup;
			SonicPickupFiltersPlugin.DebugLog(
				$"Refresh tab={_viewTab} known={known.Count} items={itemCount} search='{_searchText}' " +
				$"cat={_categoryIndex} applyCat={applyCategory} rules={PickupRuleStore.CountByMode(PickupRuleMode.Block)}B/" +
				$"{PickupRuleStore.CountByMode(PickupRuleMode.Cap)}C");

			if (groups.Count == 0)
			{
				string empty;
				if (_viewTab == ViewTab.Blocked || _viewTab == ViewTab.Capped)
				{
					bool hasRules = _viewTab == ViewTab.Blocked
						? PickupRuleStore.CountByMode(PickupRuleMode.Block) > 0
						: PickupRuleStore.CountByMode(PickupRuleMode.Cap) > 0;
					empty = !hasRules
						? (_viewTab == ViewTab.Blocked ? "No blocked items." : "No capped items.")
						: !string.IsNullOrEmpty(_searchText)
							? "No matches for search \"" + _searchText + "\". Clear search to see all."
							: (_viewTab == ViewTab.Blocked ? "No blocked items." : "No capped items.");
				}
				else if (known.Count == 0)
				{
					empty = "No known items yet — pick something up first.";
				}
				else
				{
					empty = "No matching items.";
				}

				CreateContentLabel(empty, 18, Color.white, 40f);
			}
			else
			{
				CreateColumnHeaderRow();
				float tBuild = Time.realtimeSinceStartup;
				foreach (IGrouping<ItemDrop.ItemData.ItemType, ItemDrop> group in groups)
				{
					List<ItemDrop> items = group
						.GroupBy(d => PickupRuleStore.NormalizePrefabName(((Object)d).name), StringComparer.OrdinalIgnoreCase)
						.Select(g => g.First())
						.OrderBy(d => ItemMatch.GetLocalized(d.m_itemData?.m_shared?.m_name))
						.ToList();
					CreateGroupSection(ItemMatch.FriendlyItemTypeLabel(group.Key), items);
				}

				SonicPickupFiltersPlugin.DebugLog(
					$"Row build: {(Time.realtimeSinceStartup - tBuild) * 1000f:F0}ms for {itemCount} items");
			}

			float tLayout = Time.realtimeSinceStartup;
			Canvas.ForceUpdateCanvases();
			LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);
			float preferred = Mathf.Max(
				LayoutUtility.GetPreferredHeight(_contentRoot),
				EstimateContentHeight(groups),
				80f);
			_contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _contentWidth);
			_contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferred);
			_contentRoot.localScale = Vector3.one;
			if ((Object)(object)_scrollRect != (Object)null)
			{
				if (resetScroll)
				{
					_contentRoot.anchoredPosition = Vector2.zero;
					_scrollRect.verticalNormalizedPosition = 1f;
				}
				else
				{
					_scrollRect.verticalNormalizedPosition = Mathf.Clamp01(savedNorm);
				}
			}
			else
			{
				_contentRoot.anchoredPosition = Vector2.zero;
			}

			SonicPickupFiltersPlugin.DebugLog(
				$"Layout: {(Time.realtimeSinceStartup - tLayout) * 1000f:F0}ms; " +
				$"total={(Time.realtimeSinceStartup - t0) * 1000f:F0}ms; " +
				$"children={_contentRoot.childCount}; " +
				$"content={_contentRoot.rect.width:F0}x{_contentRoot.rect.height:F0}; " +
				$"hasContentCanvas={_contentRoot.GetComponent<Canvas>() != null}");
		}

		private static void CreateContentLabel(string text, int fontSize, Color color, float height)
		{
			GameObject go = new GameObject("EmptyLabel", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
			go.transform.SetParent(_contentRoot, false);
			LayoutElement le = go.GetComponent<LayoutElement>();
			le.minHeight = height;
			le.preferredHeight = height;
			le.flexibleWidth = 1f;

			Text label = go.GetComponent<Text>();
			label.font = GUIManager.Instance.AveriaSerifBold;
			label.fontSize = fontSize;
			label.color = color;
			label.alignment = TextAnchor.MiddleCenter;
			label.text = text;
			GUIManager.Instance.ApplyTextStyle(label, fontSize);
		}

		private static float EstimateContentHeight(List<IGrouping<ItemDrop.ItemData.ItemType, ItemDrop>> groups)
		{
			if (groups == null || groups.Count == 0)
			{
				return 80f;
			}

			float height = ItemRowHeight + 8f; // column header
			for (int i = 0; i < groups.Count; i++)
			{
				int count = groups[i]
					.Select(d => PickupRuleStore.NormalizePrefabName(((Object)d).name))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.Count();
				height += CategoryHeaderHeight + count * ItemRowHeight + 8f;
			}

			return height;
		}

		private static List<ItemDrop> MergeRuleItems(List<ItemDrop> known, PickupRuleMode mode)
		{
			Dictionary<string, ItemDrop> byPrefab = new Dictionary<string, ItemDrop>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < known.Count; i++)
			{
				string prefab = PickupRuleStore.NormalizePrefabName(((Object)known[i]).name);
				if (!byPrefab.ContainsKey(prefab))
				{
					byPrefab[prefab] = known[i];
				}
			}

			if ((Object)(object)ObjectDB.instance != (Object)null)
			{
				foreach (KeyValuePair<string, PickupRule> pair in PickupRuleStore.EnumerateRules())
				{
					if (pair.Value.Mode != mode || byPrefab.ContainsKey(pair.Key))
					{
						continue;
					}

					GameObject go = ObjectDB.instance.GetItemPrefab(pair.Key);
					if ((Object)(object)go == (Object)null)
					{
						continue;
					}

					ItemDrop drop = go.GetComponent<ItemDrop>();
					if ((Object)(object)drop != (Object)null)
					{
						byPrefab[pair.Key] = drop;
					}
				}
			}

			return byPrefab.Values.ToList();
		}

		private static ItemDrop.ItemData.ItemType GetItemType(ItemDrop drop)
		{
			if (drop?.m_itemData?.m_shared == null)
			{
				return ItemDrop.ItemData.ItemType.Misc;
			}

			return drop.m_itemData.m_shared.m_itemType;
		}

		private static List<ItemDrop> GetKnownItems()
		{
			List<ItemDrop> result = new List<ItemDrop>();
			HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if ((Object)(object)ObjectDB.instance == (Object)null)
			{
				return result;
			}

			HashSet<string> known = null;
			if ((Object)(object)Player.m_localPlayer != (Object)null && KnownMaterialField != null)
			{
				known = KnownMaterialField.GetValue(Player.m_localPlayer) as HashSet<string>;
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

				string prefab = PickupRuleStore.NormalizePrefabName(((Object)go).name);
				if (!seen.Add(prefab))
				{
					continue;
				}

				string shared = drop.m_itemData.m_shared.m_name;
				if (known != null && known.Count > 0 && !known.Contains(shared))
				{
					continue;
				}

				result.Add(drop);
			}

			return result;
		}

		private static UITooltip _tooltipTemplate;

		/// <summary>
		/// Wire a Valheim UITooltip. Never AddComponent&lt;Image&gt; onto a Text (Unity allows
		/// only one Graphic), and never AddComponent&lt;UITooltip&gt; without copying setup from
		/// a live vanilla tooltip (prefab refs are otherwise null → crash on hover).
		/// </summary>
		private static void AttachTooltip(GameObject go, string topic, string body)
		{
			if ((Object)(object)go == (Object)null)
			{
				return;
			}

			try
			{
				UITooltip template = ResolveTooltipTemplate();
				if ((Object)(object)template == (Object)null)
				{
					return;
				}

				GameObject host = EnsureTooltipHost(go);
				UITooltip tip = host.GetComponent<UITooltip>();
				if ((Object)(object)tip == (Object)null)
				{
					tip = host.AddComponent<UITooltip>();
					CopyTooltipSetup(template, tip);
				}

				tip.m_topic = topic ?? string.Empty;
				tip.m_text = body ?? string.Empty;
			}
			catch (Exception ex)
			{
				SonicPickupFiltersPlugin.ModLogger?.LogWarning("AttachTooltip skipped: " + ex.Message);
			}
		}

		private static GameObject EnsureTooltipHost(GameObject go)
		{
			// Text (or TMP) already occupies the Graphic slot — put the hit target on a child.
			if ((Object)(object)go.GetComponent<Text>() != (Object)null ||
			    (Object)(object)go.GetComponent<TMPro.TMP_Text>() != (Object)null)
			{
				Transform existing = go.transform.Find("TooltipHit");
				if ((Object)(object)existing != (Object)null)
				{
					return existing.gameObject;
				}

				GameObject host = new GameObject("TooltipHit", typeof(RectTransform), typeof(Image));
				host.transform.SetParent(go.transform, false);
				RectTransform rt = host.GetComponent<RectTransform>();
				rt.anchorMin = Vector2.zero;
				rt.anchorMax = Vector2.one;
				rt.offsetMin = Vector2.zero;
				rt.offsetMax = Vector2.zero;
				Image img = host.GetComponent<Image>();
				img.color = new Color(0f, 0f, 0f, 0.01f);
				img.raycastTarget = true;
				return host;
			}

			Graphic graphic = go.GetComponent<Graphic>();
			if ((Object)(object)graphic != (Object)null)
			{
				graphic.raycastTarget = true;
			}

			return go;
		}

		private static UITooltip ResolveTooltipTemplate()
		{
			if ((Object)(object)_tooltipTemplate != (Object)null)
			{
				return _tooltipTemplate;
			}

			if ((Object)(object)InventoryGui.instance != (Object)null)
			{
				Button repair = InventoryGui.instance.m_repairButton;
				if ((Object)(object)repair != (Object)null)
				{
					UITooltip repairTip = repair.GetComponent<UITooltip>();
					if ((Object)(object)repairTip != (Object)null)
					{
						_tooltipTemplate = repairTip;
						return _tooltipTemplate;
					}
				}

				UITooltip[] inventoryTips = InventoryGui.instance.GetComponentsInChildren<UITooltip>(true);
				if (inventoryTips != null && inventoryTips.Length > 0)
				{
					_tooltipTemplate = inventoryTips[0];
					return _tooltipTemplate;
				}
			}

			UITooltip[] all = Resources.FindObjectsOfTypeAll<UITooltip>();
			if (all != null)
			{
				for (int i = 0; i < all.Length; i++)
				{
					UITooltip candidate = all[i];
					if ((Object)(object)candidate == (Object)null)
					{
						continue;
					}

					// Prefer scene objects that already work in the inventory HUD.
					if (candidate.gameObject.scene.IsValid())
					{
						_tooltipTemplate = candidate;
						return _tooltipTemplate;
					}
				}
			}

			return null;
		}

		private static void CopyTooltipSetup(UITooltip source, UITooltip dest)
		{
			if ((Object)(object)source == (Object)null || (Object)(object)dest == (Object)null)
			{
				return;
			}

			List<FieldInfo> fields = AccessTools.GetDeclaredFields(typeof(UITooltip));
			for (int i = 0; i < fields.Count; i++)
			{
				FieldInfo field = fields[i];
				if (field.IsStatic || field.IsLiteral)
				{
					continue;
				}

				// Content is set by the caller.
				if (field.Name == "m_topic" || field.Name == "m_text")
				{
					continue;
				}

				try
				{
					field.SetValue(dest, field.GetValue(source));
				}
				catch
				{
					// Ignore fields Unity refuses to copy.
				}
			}
		}

		private static void SetCapPlaceholder(InputField field, bool capOn)
		{
			if ((Object)(object)field == (Object)null)
			{
				return;
			}

			string text = capOn
				? ModLocalization.Translate(ModLocalization.CapPlaceholderOn, "max stack")
				: ModLocalization.Translate(ModLocalization.CapPlaceholderOff, "check Cap");

			if ((Object)(object)field.placeholder != (Object)null)
			{
				Text placeholder = field.placeholder as Text;
				if ((Object)(object)placeholder != (Object)null)
				{
					placeholder.text = text;
					placeholder.fontStyle = FontStyle.Italic;
					placeholder.color = new Color(1f, 1f, 1f, 0.35f);
				}
			}
		}

		private static void CreateGroupSection(string title, List<ItemDrop> items)
		{
			float spacing = 2f;
			float padBottom = 4f;
			float sectionH = CategoryHeaderHeight + items.Count * (ItemRowHeight + spacing) + padBottom;

			GameObject section = new GameObject(
				"Section_" + title,
				typeof(RectTransform),
				typeof(LayoutElement),
				typeof(VerticalLayoutGroup));
			section.transform.SetParent(_contentRoot, false);

			LayoutElement sectionLe = section.GetComponent<LayoutElement>();
			sectionLe.flexibleWidth = 1f;
			sectionLe.minHeight = sectionH;
			sectionLe.preferredHeight = sectionH;
			sectionLe.flexibleHeight = 0f;

			VerticalLayoutGroup vlg = section.GetComponent<VerticalLayoutGroup>();
			vlg.childAlignment = TextAnchor.UpperLeft;
			vlg.childControlHeight = true;
			vlg.childControlWidth = true;
			vlg.childForceExpandHeight = false;
			vlg.childForceExpandWidth = true;
			vlg.spacing = spacing;
			vlg.padding = new RectOffset(0, 0, 0, (int)padBottom);

			GameObject headerGo = GUIManager.Instance.CreateText(
				text: title,
				parent: section.transform,
				anchorMin: new Vector2(0f, 1f),
				anchorMax: new Vector2(1f, 1f),
				position: Vector2.zero,
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 16,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: RowInnerW,
				height: CategoryHeaderHeight,
				addContentSizeFitter: false);
			headerGo.name = "Header";
			LayoutElement headerLe = headerGo.GetComponent<LayoutElement>() ?? headerGo.AddComponent<LayoutElement>();
			headerLe.minHeight = CategoryHeaderHeight;
			headerLe.preferredHeight = CategoryHeaderHeight;
			headerLe.flexibleHeight = 0f;
			headerLe.flexibleWidth = 1f;
			Text headerText = headerGo.GetComponent<Text>();
			if ((Object)(object)headerText != (Object)null)
			{
				headerText.alignment = TextAnchor.MiddleLeft;
				headerText.raycastTarget = false;
			}

			for (int i = 0; i < items.Count; i++)
			{
				CreateItemRow(section.transform, items[i]);
			}
		}

		private static GameObject CreateTableRowRoot(string name, float height)
		{
			GameObject row = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
			row.transform.SetParent(_contentRoot, false);

			LayoutElement le = row.GetComponent<LayoutElement>();
			le.minHeight = height;
			le.preferredHeight = height;
			le.flexibleHeight = 0f;
			le.flexibleWidth = 1f;

			RectTransform rt = row.GetComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 1f);
			rt.anchorMax = new Vector2(1f, 1f);
			rt.pivot = new Vector2(0.5f, 1f);
			rt.sizeDelta = new Vector2(0f, height);
			return row;
		}

		private static void CreateColumnHeaderRow()
		{
			GameObject row = CreateTableRowRoot("ColumnHeaders", ItemRowHeight);

			GameObject itemLabel = PlaceHeaderLabel(
				row.transform,
				ModLocalization.Translate(ModLocalization.ColItem, "Item"),
				ColNameX,
				ColNameW,
				TextAnchor.MiddleLeft);
			AttachTooltip(
				itemLabel,
				ModLocalization.Translate(ModLocalization.TipItemTopic, "Item"),
				ModLocalization.Translate(
					ModLocalization.TipItemBody,
					"Discovered items from your world. Use Block + Cap to control auto-pickup."));

			GameObject blockLabel = PlaceHeaderLabel(
				row.transform,
				ModLocalization.Translate(ModLocalization.ColBlock, "Block"),
				ColBlockX,
				ToggleSize + 16f,
				TextAnchor.MiddleCenter);
			AttachTooltip(
				blockLabel,
				ModLocalization.Translate(ModLocalization.TipBlockTopic, "Block"),
				ModLocalization.Translate(
					ModLocalization.TipBlockBody,
					"Never auto-pick this item. Manual Use still works. Mutually exclusive with Cap."));

			GameObject capLabel = PlaceHeaderLabel(
				row.transform,
				ModLocalization.Translate(ModLocalization.ColCap, "Cap"),
				ColCapToggleX,
				ToggleSize + 16f,
				TextAnchor.MiddleCenter);
			AttachTooltip(
				capLabel,
				ModLocalization.Translate(ModLocalization.TipCapTopic, "Cap"),
				ModLocalization.Translate(
					ModLocalization.TipCapBody,
					"Check Cap to limit auto-pickup. Defaults to the item's max stack — change Qty afterward."));

			GameObject qtyLabel = PlaceHeaderLabel(
				row.transform,
				ModLocalization.Translate(ModLocalization.ColCapQty, "Qty"),
				ColCapQtyX,
				CapFieldW,
				TextAnchor.MiddleCenter);
			AttachTooltip(
				qtyLabel,
				ModLocalization.Translate(ModLocalization.TipCapQtyTopic, "Qty"),
				ModLocalization.Translate(
					ModLocalization.TipCapQtyBody,
					"Max auto-pickup count while Cap is checked. Empty/invalid restores max stack."));
		}

		private static GameObject PlaceHeaderLabel(
			Transform parent,
			string text,
			float x,
			float width,
			TextAnchor align)
		{
			GameObject go = GUIManager.Instance.CreateText(
				text: text,
				parent: parent,
				anchorMin: new Vector2(0f, 0.5f),
				anchorMax: new Vector2(0f, 0.5f),
				position: new Vector2(x + width * 0.5f, 0f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 14,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: width,
				height: ItemRowHeight,
				addContentSizeFitter: false);
			Text label = go.GetComponent<Text>();
			if ((Object)(object)label != (Object)null)
			{
				label.alignment = align;
				label.raycastTarget = false;
			}

			return go;
		}

		private static void PlaceLabel(
			Transform parent,
			string text,
			float x,
			float width,
			int fontSize,
			Color color,
			TextAnchor align)
		{
			GameObject go = GUIManager.Instance.CreateText(
				text: text,
				parent: parent,
				anchorMin: new Vector2(0f, 0.5f),
				anchorMax: new Vector2(0f, 0.5f),
				position: new Vector2(x + width * 0.5f, 0f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: fontSize,
				color: color,
				outline: true,
				outlineColor: Color.black,
				width: width,
				height: ItemRowHeight,
				addContentSizeFitter: false);
			Text label = go.GetComponent<Text>();
			if ((Object)(object)label != (Object)null)
			{
				label.alignment = align;
				label.raycastTarget = false;
				label.horizontalOverflow = HorizontalWrapMode.Overflow;
				label.verticalOverflow = VerticalWrapMode.Truncate;
			}
		}

		private static void CreateItemRow(Transform parent, ItemDrop item)
		{
			string prefab = PickupRuleStore.NormalizePrefabName(((Object)item).name);
			PickupRule rule = PickupRuleStore.GetRule(prefab);
			string fullName = ItemMatch.GetLocalized(item.m_itemData?.m_shared?.m_name);
			if (string.IsNullOrEmpty(fullName))
			{
				fullName = prefab;
			}

			int defaultCap = GetDefaultCap(item);
			bool blockOn = rule.Mode == PickupRuleMode.Block;
			bool capOn = rule.Mode == PickupRuleMode.Cap;

			GameObject row = new GameObject("Row_" + prefab, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
			row.transform.SetParent(parent, false);

			LayoutElement rowLe = row.GetComponent<LayoutElement>();
			rowLe.minHeight = ItemRowHeight;
			rowLe.preferredHeight = ItemRowHeight;
			rowLe.flexibleHeight = 0f;
			rowLe.flexibleWidth = 1f;

			RectTransform rowRt = row.GetComponent<RectTransform>();
			rowRt.localScale = Vector3.one;

			Image rowBg = row.GetComponent<Image>();
			rowBg.color = new Color(0f, 0f, 0f, 0.18f);
			rowBg.raycastTarget = false;

			Image icon = CreateIcon(row.transform, item, rule);

			GameObject nameGo = GUIManager.Instance.CreateText(
				text: fullName,
				parent: row.transform,
				anchorMin: new Vector2(0f, 0.5f),
				anchorMax: new Vector2(0f, 0.5f),
				position: new Vector2(ColNameX + ColNameW * 0.5f, 0f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 14,
				color: Color.white,
				outline: true,
				outlineColor: Color.black,
				width: ColNameW,
				height: ItemRowHeight - 4f,
				addContentSizeFitter: false);
			nameGo.name = "Name";
			Text nameText = nameGo.GetComponent<Text>();
			if ((Object)(object)nameText != (Object)null)
			{
				nameText.alignment = TextAnchor.MiddleLeft;
				nameText.raycastTarget = false;
				nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
				nameText.verticalOverflow = VerticalWrapMode.Truncate;
			}

			// Jötunn CreateToggle parents with worldPositionStays=true — reparent false + scale=1.
			GameObject blockToggleGo = GUIManager.Instance.CreateToggle(row.transform, ToggleSize, ToggleSize);
			blockToggleGo.name = "BlockToggle";
			FixJotunnToggle(blockToggleGo, row.transform, ColBlockX + ToggleSize * 0.5f, 0f, ToggleSize);
			Toggle blockToggle = blockToggleGo.GetComponent<Toggle>();
			if ((Object)(object)blockToggle != (Object)null)
			{
				GUIManager.Instance.ApplyToogleStyle(blockToggle);
			}

			GameObject capToggleGo = GUIManager.Instance.CreateToggle(row.transform, ToggleSize, ToggleSize);
			capToggleGo.name = "CapToggle";
			FixJotunnToggle(capToggleGo, row.transform, ColCapToggleX + ToggleSize * 0.5f, 0f, ToggleSize);
			Toggle capToggle = capToggleGo.GetComponent<Toggle>();
			if ((Object)(object)capToggle != (Object)null)
			{
				GUIManager.Instance.ApplyToogleStyle(capToggle);
			}

			GameObject capInputGo = GUIManager.Instance.CreateInputField(
				parent: row.transform,
				anchorMin: new Vector2(0f, 0.5f),
				anchorMax: new Vector2(0f, 0.5f),
				position: new Vector2(ColCapQtyX + CapFieldW * 0.5f, 0f),
				contentType: InputField.ContentType.IntegerNumber,
				placeholderText: string.Empty,
				fontSize: 14,
				width: CapFieldW,
				height: CapFieldH);
			capInputGo.name = "CapInput";
			capInputGo.transform.localScale = Vector3.one;
			InputField capInput = capInputGo.GetComponent<InputField>();
			if ((Object)(object)capInput != (Object)null)
			{
				GUIManager.Instance.ApplyInputFieldStyle(capInput, 14);
				capInput.interactable = capOn;
			}

			string capturedPrefab = prefab;
			int capturedDefaultCap = defaultCap;
			Image capturedIcon = icon;
			Toggle capturedBlock = blockToggle;
			Toggle capturedCapToggle = capToggle;
			InputField capturedCap = capInput;

			bool suppress = true;

			void ApplyAllow()
			{
				PickupRuleStore.SetRule(capturedPrefab, PickupRule.Allow);
				TintIcon(capturedIcon, ColNormal);
				if ((Object)(object)capturedCap != (Object)null)
				{
					capturedCap.interactable = false;
					capturedCap.text = string.Empty;
					SetCapPlaceholder(capturedCap, false);
				}
			}

			void ApplyBlock()
			{
				PickupRuleStore.SetRule(capturedPrefab, PickupRule.Block);
				TintIcon(capturedIcon, ColBlocked);
				if ((Object)(object)capturedCap != (Object)null)
				{
					capturedCap.interactable = false;
					capturedCap.text = string.Empty;
					SetCapPlaceholder(capturedCap, false);
				}
			}

			void ApplyCap(int amount)
			{
				int cap = Math.Max(1, amount);
				PickupRuleStore.SetRule(capturedPrefab, PickupRule.CapAt(cap));
				TintIcon(capturedIcon, ColCapped);
				if ((Object)(object)capturedCap != (Object)null)
				{
					capturedCap.interactable = true;
					capturedCap.text = cap.ToString();
					SetCapPlaceholder(capturedCap, true);
				}
			}

			void AfterRuleChange()
			{
				UpdateTabLabels();
				if (_viewTab != ViewTab.All)
				{
					_refreshQueued = true;
				}
			}

			if ((Object)(object)blockToggle != (Object)null)
			{
				blockToggle.isOn = blockOn;
				blockToggle.onValueChanged.AddListener(isOn =>
				{
					if (suppress)
					{
						return;
					}

					suppress = true;
					if (isOn)
					{
						if ((Object)(object)capturedCapToggle != (Object)null)
						{
							capturedCapToggle.isOn = false;
						}

						ApplyBlock();
					}
					else if ((Object)(object)capturedCapToggle != (Object)null && capturedCapToggle.isOn)
					{
						// Cap still on — keep Cap mode.
					}
					else
					{
						ApplyAllow();
					}

					suppress = false;
					AfterRuleChange();
				});
			}

			if ((Object)(object)capToggle != (Object)null)
			{
				capToggle.isOn = capOn;
				capToggle.onValueChanged.AddListener(isOn =>
				{
					if (suppress)
					{
						return;
					}

					suppress = true;
					if (isOn)
					{
						if ((Object)(object)capturedBlock != (Object)null)
						{
							capturedBlock.isOn = false;
						}

						int amount = capturedDefaultCap;
						if ((Object)(object)capturedCap != (Object)null &&
						    int.TryParse(capturedCap.text, out int existing) &&
						    existing > 0)
						{
							amount = existing;
						}

						ApplyCap(amount);
					}
					else if ((Object)(object)capturedBlock != (Object)null && capturedBlock.isOn)
					{
						ApplyBlock();
					}
					else
					{
						ApplyAllow();
					}

					suppress = false;
					AfterRuleChange();
				});
			}

			if ((Object)(object)capInput != (Object)null)
			{
				capInput.text = capOn ? rule.Cap.ToString() : string.Empty;
				SetCapPlaceholder(capInput, capOn);
				capInput.onEndEdit.AddListener(text =>
				{
					if (suppress)
					{
						return;
					}

					if ((Object)(object)capturedCapToggle == (Object)null || !capturedCapToggle.isOn)
					{
						suppress = true;
						capturedCap.text = string.Empty;
						SetCapPlaceholder(capturedCap, false);
						suppress = false;
						return;
					}

					if (string.IsNullOrWhiteSpace(text))
					{
						// Empty while Cap is on → restore default (max stack), do not fall back to Block.
						suppress = true;
						ApplyCap(capturedDefaultCap);
						suppress = false;
					}
					else if (int.TryParse(text, out int capVal) && capVal > 0)
					{
						ApplyCap(capVal);
					}
					else
					{
						suppress = true;
						ApplyCap(capturedDefaultCap);
						suppress = false;
					}

					AfterRuleChange();
				});
			}

			suppress = false;
		}

		/// <summary>
		/// Jötunn CreateToggle uses SetParent(parent) without worldPositionStays:false, which
		/// inherits world scale under nested scroll content and draws huge checkboxes.
		/// </summary>
		private static void FixJotunnToggle(GameObject toggleGo, Transform parent, float x, float y, float size)
		{
			if ((Object)(object)toggleGo == (Object)null)
			{
				return;
			}

			toggleGo.transform.SetParent(parent, false);
			toggleGo.transform.localScale = Vector3.one;
			toggleGo.transform.localRotation = Quaternion.identity;

			RectTransform rt = toggleGo.GetComponent<RectTransform>();
			if ((Object)(object)rt != (Object)null)
			{
				rt.anchorMin = new Vector2(0f, 0.5f);
				rt.anchorMax = new Vector2(0f, 0.5f);
				rt.pivot = new Vector2(0.5f, 0.5f);
				rt.anchoredPosition = new Vector2(x, y);
				rt.sizeDelta = new Vector2(size, size);
			}

			Transform bg = toggleGo.transform.Find("Background");
			if ((Object)(object)bg != (Object)null)
			{
				RectTransform bgRt = bg.GetComponent<RectTransform>();
				if ((Object)(object)bgRt != (Object)null)
				{
					bgRt.anchorMin = Vector2.zero;
					bgRt.anchorMax = Vector2.one;
					bgRt.offsetMin = Vector2.zero;
					bgRt.offsetMax = Vector2.zero;
					bgRt.localScale = Vector3.one;
				}

				Transform check = bg.Find("Checkmark");
				if ((Object)(object)check != (Object)null)
				{
					RectTransform ckRt = check.GetComponent<RectTransform>();
					if ((Object)(object)ckRt != (Object)null)
					{
						ckRt.anchorMin = Vector2.zero;
						ckRt.anchorMax = Vector2.one;
						ckRt.offsetMin = Vector2.zero;
						ckRt.offsetMax = Vector2.zero;
						ckRt.localScale = Vector3.one;
					}
				}
			}
		}

		private static void TintIcon(Image icon, Color color)
		{
			if ((Object)(object)icon != (Object)null)
			{
				icon.color = color;
			}
		}

		private static Image CreateIcon(Transform parent, ItemDrop item, PickupRule rule)
		{
			// Holder clips oversized sprites to IconSize×IconSize.
			GameObject holder = new GameObject("Icon", typeof(RectTransform), typeof(RectMask2D));
			holder.transform.SetParent(parent, false);
			RectTransform holderRt = holder.GetComponent<RectTransform>();
			holderRt.anchorMin = new Vector2(0f, 0.5f);
			holderRt.anchorMax = new Vector2(0f, 0.5f);
			holderRt.pivot = new Vector2(0.5f, 0.5f);
			holderRt.anchoredPosition = new Vector2(ColIconX + IconSize * 0.5f, 0f);
			holderRt.sizeDelta = new Vector2(IconSize, IconSize);
			holderRt.localScale = Vector3.one;

			GameObject iconGo = new GameObject("Sprite", typeof(RectTransform), typeof(Image));
			iconGo.transform.SetParent(holder.transform, false);
			RectTransform iconRt = iconGo.GetComponent<RectTransform>();
			iconRt.anchorMin = Vector2.zero;
			iconRt.anchorMax = Vector2.one;
			iconRt.offsetMin = Vector2.zero;
			iconRt.offsetMax = Vector2.zero;
			iconRt.localScale = Vector3.one;

			Image icon = iconGo.GetComponent<Image>();
			Sprite sprite = GetItemSprite(item);
			if ((Object)(object)sprite != (Object)null)
			{
				icon.sprite = sprite;
				icon.preserveAspect = true;
			}

			icon.color = rule.Mode == PickupRuleMode.Block
				? ColBlocked
				: rule.Mode == PickupRuleMode.Cap
					? ColCapped
					: ColNormal;
			icon.raycastTarget = false;
			return icon;
		}

		private static void OpenCapEditor(string prefab, string displayName, int defaultCap)
		{
			_capEditPrefab = prefab;
			_capEditDefault = Math.Max(1, defaultCap);
			PickupRule current = PickupRuleStore.GetRule(prefab);
			int value = current.Mode == PickupRuleMode.Cap ? current.Cap : _capEditDefault;
			if (current.Mode != PickupRuleMode.Cap)
			{
				PickupRuleStore.SetRule(prefab, PickupRule.CapAt(value));
			}

			EnsureCapEditor();
			if ((Object)(object)_capEditorTitle != (Object)null)
			{
				_capEditorTitle.text = string.IsNullOrEmpty(displayName) ? prefab : displayName;
			}

			if ((Object)(object)_capInput != (Object)null)
			{
				_capInput.text = value.ToString();
				_capInput.ActivateInputField();
				_capInput.Select();
			}

			_capEditor.SetActive(true);
			_capEditor.transform.SetAsLastSibling();
			UpdateTabLabels();
		}

		private static void EnsureCapEditor()
		{
			if ((Object)(object)_capEditor != (Object)null || (Object)(object)_panel == (Object)null)
			{
				return;
			}

			_capEditor = GUIManager.Instance.CreateWoodpanel(
				parent: _panel.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: Vector2.zero,
				width: 380f,
				height: 240f,
				draggable: true);
			_capEditor.SetActive(false);

			GameObject titleGo = GUIManager.Instance.CreateText(
				text: "Cap",
				parent: _capEditor.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -28f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 22,
				color: GUIManager.Instance.ValheimOrange,
				outline: true,
				outlineColor: Color.black,
				width: 320f,
				height: 30f,
				addContentSizeFitter: false);
			_capEditorTitle = titleGo.GetComponent<Text>();

			GUIManager.Instance.CreateText(
				text: "Max auto-pickup count",
				parent: _capEditor.transform,
				anchorMin: new Vector2(0.5f, 1f),
				anchorMax: new Vector2(0.5f, 1f),
				position: new Vector2(0f, -60f),
				font: GUIManager.Instance.AveriaSerifBold,
				fontSize: 13,
				color: Color.white,
				outline: true,
				outlineColor: Color.black,
				width: 300f,
				height: 20f,
				addContentSizeFitter: false);

			GameObject inputGo = GUIManager.Instance.CreateInputField(
				parent: _capEditor.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: new Vector2(0f, 20f),
				contentType: InputField.ContentType.IntegerNumber,
				placeholderText: "50",
				fontSize: 18,
				width: 140f,
				height: 36f);
			_capInput = inputGo.GetComponent<InputField>();

			CreateCapButton("-1", new Vector2(-120f, 20f), () => NudgeCap(-1));
			CreateCapButton("+1", new Vector2(120f, 20f), () => NudgeCap(1));
			CreateCapButton("-stack", new Vector2(-120f, -30f), () => NudgeCap(-_capEditDefault));
			CreateCapButton("+stack", new Vector2(120f, -30f), () => NudgeCap(_capEditDefault));

			GameObject applyGo = GUIManager.Instance.CreateButton(
				text: "Apply",
				parent: _capEditor.transform,
				anchorMin: new Vector2(0.5f, 0f),
				anchorMax: new Vector2(0.5f, 0f),
				position: new Vector2(-70f, 36f),
				width: 120f,
				height: 40f);
			applyGo.GetComponent<Button>().onClick.AddListener(ApplyCapEditor);

			GameObject cancelGo = GUIManager.Instance.CreateButton(
				text: "Cancel",
				parent: _capEditor.transform,
				anchorMin: new Vector2(0.5f, 0f),
				anchorMax: new Vector2(0.5f, 0f),
				position: new Vector2(70f, 36f),
				width: 120f,
				height: 40f);
			cancelGo.GetComponent<Button>().onClick.AddListener(HideCapEditor);
		}

		private static void CreateCapButton(string label, Vector2 pos, UnityEngine.Events.UnityAction action)
		{
			GameObject go = GUIManager.Instance.CreateButton(
				text: label,
				parent: _capEditor.transform,
				anchorMin: new Vector2(0.5f, 0.5f),
				anchorMax: new Vector2(0.5f, 0.5f),
				position: pos,
				width: 90f,
				height: 32f);
			go.GetComponent<Button>().onClick.AddListener(action);
		}

		private static void NudgeCap(int delta)
		{
			if ((Object)(object)_capInput == (Object)null)
			{
				return;
			}

			if (!int.TryParse(_capInput.text, out int value))
			{
				value = _capEditDefault;
			}

			_capInput.text = Math.Max(1, value + delta).ToString();
		}

		private static void ApplyCapEditor()
		{
			if (string.IsNullOrEmpty(_capEditPrefab) || (Object)(object)_capInput == (Object)null)
			{
				HideCapEditor();
				return;
			}

			if (!int.TryParse(_capInput.text, out int value) || value < 1)
			{
				value = Math.Max(1, _capEditDefault);
			}

			PickupRuleStore.SetRule(_capEditPrefab, PickupRule.CapAt(value));
			HideCapEditor();
			RefreshIfOpen();
		}

		private static void HideCapEditor()
		{
			if ((Object)(object)_capEditor != (Object)null)
			{
				_capEditor.SetActive(false);
			}

			_capEditPrefab = string.Empty;
		}

		private static int GetDefaultCap(ItemDrop item)
		{
			if (SonicPickupFiltersPlugin.DefaultCapUsesMaxStack == null ||
			    !SonicPickupFiltersPlugin.DefaultCapUsesMaxStack.Value)
			{
				return 50;
			}

			return Math.Max(1, item?.m_itemData?.m_shared?.m_maxStackSize ?? 1);
		}

		private static Sprite GetItemSprite(ItemDrop item)
		{
			string key = ((Object)item).name;
			if (IconCache.TryGetValue(key, out Sprite cached))
			{
				return cached;
			}

			// Prefer shared icons — GetIcon() can do extra work on first resolve.
			Sprite sprite = null;
			if (item.m_itemData?.m_shared?.m_icons != null &&
			    item.m_itemData.m_shared.m_icons.Length > 0)
			{
				sprite = item.m_itemData.m_shared.m_icons[0];
			}

			if ((Object)(object)sprite == (Object)null)
			{
				try
				{
					if (item.m_itemData != null)
					{
						sprite = item.m_itemData.GetIcon();
					}
				}
				catch
				{
					sprite = null;
				}
			}

			IconCache[key] = sprite;
			return sprite;
		}
	}
}
