﻿using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UILib.ContextMenu;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;
using Type = System.Type;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if KOIKATSU || SUNSHINE
using Expression = ExpressionBone;
using ExtensibleSaveFormat;
using Sideloader.AutoResolver;
#elif AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
#endif

#if FEATURE_AUTOGEN
using System.Diagnostics;
using UniRx;
using UniRx.Triggers;
#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif
#endif

// v096
namespace Timeline
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
    [BepInDependency(Sideloader.Sideloader.GUID, Sideloader.Sideloader.Version)]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency(KKAPI.KoikatuAPI.GUID, KKAPI.KoikatuAPI.VersionConst)]
#endif
    public class Timeline : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "Timeline";
        public const string Version = "1.5.1";
        public const string GUID = "com.joan6694.illusionplugins.timeline";
        internal const string _ownerId = "Timeline";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "timeline";
#if FEATURE_SOUND
        private const string _extSaveKey2 = "timelineSound";
#endif
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        private class HeaderDisplay
        {
            public GameObject gameObject;
            public LayoutElement layoutElement;
            public RectTransform container;
            public Text name;
            public InputField inputField;

            public bool expanded = true;
            public GroupNode<InterpolableGroup> group;
        }

        public class InterpolableDisplay
        {
            public GameObject gameObject;
            public LayoutElement layoutElement;
            public RectTransform container;
            public CanvasGroup group;
            public Toggle enabled;
            public Text name;
            public InputField inputField;
            public Image background;
            public Image selectedOutline;
            public RawImage gridBackground;

            public LeafNode<Interpolable> interpolable;
        }

        public class InterpolableModelDisplay
        {
            public GameObject gameObject;
            public LayoutElement layoutElement;
            public Text name;

            public InterpolableModel model;
        }

        public class KeyframeDisplay
        {
            public GameObject gameObject;
            public RawImage image;

            public Keyframe keyframe;
        }

        private class CurveKeyframeDisplay
        {
            public GameObject gameObject;
            public RawImage image;
            public PointerDownHandler pointerDownHandler;
            public ScrollHandler scrollHandler;
            public DragHandler dragHandler;
            public PointerEnterHandler pointerEnterHandler;
        }

        private class SingleFileDisplay
        {
            public Toggle toggle;
            public Text text;
        }

        public class InterpolableGroup
        {
            public string name;
            public bool expanded = true;
        }

#if FEATURE_UNDO
        [System.Serializable]
        public class TransactionData
        {        
            public ObjectCtrlInfo ctrlInfo;
            public string data;

            // 생성자
            public TransactionData(ObjectCtrlInfo _ctrlInfo, string _data)
            {
                ctrlInfo = _ctrlInfo;
                data = _data;
            }
        }
        
        public class LimitedStack<T> : Stack<T>
        {
            private int _maxSize;

            public LimitedStack(int maxSize)
            {
                _maxSize = maxSize;
            }

            public new void Push(T item)
            {
                // 최대 크기 초과 시, 가장 오래된 항목 제거
                if (Count >= _maxSize)
                {
                    // Stack에는 직접 Queue처럼 제거할 수 없으므로,
                    // List로 옮겨서 가장 오래된 항목 제거 후 다시 Stack으로 재구성
                    var tempList = new List<T>(this);
                    tempList.Reverse(); // Stack 순서대로 정렬
                    tempList.RemoveAt(0); // 가장 오래된 항목 제거
                    Clear();
                    foreach (var t in tempList)
                        base.Push(t);
                }

                base.Push(item);
            }
        }        
#endif
#if FEATURE_SOUND
        public class TimerItem
        {
            public Coroutine coroutine;
            public Interpolable interpolable;
        }

        [System.Serializable]
        public class SoundItem
        {
            // public string sceneName;
            public string fileName;
            public float  volume;
        }
#endif 
#if FIXED_096
       public class ObjectCtrlItem {
            public ObjectCtrlInfo oci;
            public GameObject keyframeGroup; 
            public List<KeyframeDisplay> displayedKeyframes;
            public bool dirty;
       }
#endif
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static Timeline _self;
        private static string _assemblyLocation;
        private static string _singleFilesFolder;
        private static bool _refreshInterpolablesListScheduled = false;
        private bool _loaded = false;
        private int _totalActiveExpressions = 0;
        private int _currentExpressionIndex = 0;
        private readonly HashSet<Expression> _allExpressions = new HashSet<Expression>();
        internal List<InterpolableModel> _interpolableModelsList = new List<InterpolableModel>();
        internal Dictionary<string, List<InterpolableModel>> _interpolableModelsDictionary = new Dictionary<string, List<InterpolableModel>>();
        private readonly Dictionary<string, int> _hardCodedOwnerOrder = new Dictionary<string, int>()
        {
            {_ownerId, 0},
            {"HSPE", 1},
            {"KKPE", 1},
            {"RendererEditor", 2},
            {"NodesConstraints", 3}
        };
        internal Dictionary<Transform, GuideObject> _allGuideObjects;
        internal HashSet<GuideObject> _selectedGuideObjects;
        private readonly List<Interpolable> _toDelete = new List<Interpolable>();
        private readonly Dictionary<int, Interpolable> _interpolables = new Dictionary<int, Interpolable>();
        private readonly Tree<Interpolable, InterpolableGroup> _interpolablesTree = new Tree<Interpolable, InterpolableGroup>();

        private const float _baseGridWidth = 300f;
        private const int _interpolableMaxHeight = 32;
        private const int _interpolableMinHeight = 15;
        private int interpolableHeight = _interpolableMaxHeight;
        private const float _curveGridCellSizePercent = 1f / 24f;
        private Canvas _ui;
        private Sprite _linkSprite;
        private Sprite _colorSprite;
        private Sprite _renameSprite;
        private Sprite _newFolderSprite;
        private Sprite _addSprite;
        private Sprite _addToFolderSprite;
        private Sprite _chevronUpSprite;
        private Sprite _chevronDownSprite;
        private Sprite _deleteSprite;
        private Sprite _checkboxSprite;
        private Sprite _checkboxCompositeSprite;
        private Sprite _selectAllSprite;

        private RectTransform _timelineWindow;
        private GameObject _helpPanel;
        private RectTransform _cursor;
        private RectTransform _grid;
        private RawImage _gridImage;
        private RectTransform _gridTop;
        private bool _isDraggingCursor;
        private ScrollRect _verticalScrollView;
        private ScrollRect _horizontalScrollView;
        private Toggle _allToggle;
        private InputField _interpolablesSearchField;
        private Regex _interpolablesSearchRegex;
        private InputField _frameRateInputField;
        private InputField _timeInputField;
        private InputField _durationInputField;
        private InputField _blockLengthInputField;
        private InputField _divisionsInputField;
        private InputField _speedInputField;
        private GameObject _singleFilePrefab;
        private GameObject _singleFilesPanel;
        private RectTransform _singleFilesContainer;
        private InputField _singleFileNameField;
        private readonly List<SingleFileDisplay> _displayedSingleFiles = new List<SingleFileDisplay>();
        private float _zoomLevel = 1f;
        private RectTransform _textsContainer;
        private readonly List<Text> _timeTexts = new List<Text>();
        private RectTransform _resizeHandle;
        private GameObject _keyframeWindow;
        private Text _keyframeInterpolableNameText;
        private Button _keyframeSelectPrevButton;
        private Button _keyframeSelectNextButton;
        private InputField _keyframeTimeTextField;
        private Button _keyframeUseCurrentTimeButton;
        private Text _keyframeValueText;
        private Button _keyframeUseCurrentValueButton;
        private Text _keyframeDeleteButtonText;
        private GameObject _headerPrefab;
        private readonly List<HeaderDisplay> _displayedOwnerHeader = new List<HeaderDisplay>();
        private GameObject _interpolablePrefab;
        private GameObject _interpolableModelPrefab;
        private readonly List<InterpolableDisplay> _displayedInterpolables = new List<InterpolableDisplay>();
        private readonly List<InterpolableModelDisplay> _displayedInterpolableModels = new List<InterpolableModelDisplay>();
        private readonly List<float> _gridHeights = new List<float>();
        private readonly List<RawImage> _interpolableSeparators = new List<RawImage>();
        private RectTransform _keyframesContainer;
        private RectTransform _miscContainer;
        private GameObject _keyframePrefab;

        private Material _keyframesBackgroundMaterial;
        private Text _tooltip;
        private GameObject _curveKeyframePrefab;
        private RawImage _curveContainer;
        private readonly Texture2D _curveTexture = new Texture2D(512, 1, TextureFormat.RFloat, false, true);
        private InputField _curveTimeInputField;
        private Slider _curveTimeSlider;
        private InputField _curveValueInputField;
        private Slider _curveValueSlider;
        private InputField _curveInTangentInputField;
        private Slider _curveInTangentSlider;
        private InputField _curveOutTangentInputField;
        private Slider _curveOutTangentSlider;
        private RectTransform _cursor2;
        private readonly List<CurveKeyframeDisplay> _displayedCurveKeyframes = new List<CurveKeyframeDisplay>();
        private readonly AnimationCurve _linePreset = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        private readonly AnimationCurve _topPreset = new AnimationCurve(new UnityEngine.Keyframe(0f, 0f, 2f, 2f), new UnityEngine.Keyframe(1f, 1f, 0f, 0f));
        private readonly AnimationCurve _bottomPreset = new AnimationCurve(new UnityEngine.Keyframe(0f, 0f, 0f, 0f), new UnityEngine.Keyframe(1f, 1f, 2f, 2f));
        private readonly AnimationCurve _hermitePreset = new AnimationCurve(new UnityEngine.Keyframe(0f, 0f, 0f, 0f), new UnityEngine.Keyframe(1f, 1f, 0f, 0f));
        private readonly AnimationCurve _stairsPreset = new AnimationCurve(new UnityEngine.Keyframe(0f, 0f, 0f, 0f), new UnityEngine.Keyframe(1f, 1f, float.PositiveInfinity, 0f));

        private bool _isPlaying;
        private float _startTime;
        private float _playbackTime;
        private float _duration = 10f;
        private float _blockLength = 10f;
        private int _divisions = 10;
        private int _desiredFrameRate = 60;
        private readonly List<Interpolable> _selectedInterpolables = new List<Interpolable>();
        private readonly List<KeyValuePair<float, Keyframe>> _selectedKeyframes = new List<KeyValuePair<float, Keyframe>>();
        private readonly List<KeyValuePair<float, Keyframe>> _copiedKeyframes = new List<KeyValuePair<float, Keyframe>>();
        private readonly List<KeyValuePair<float, Keyframe>> _cutKeyframes = new List<KeyValuePair<float, Keyframe>>();

        private readonly Dictionary<KeyframeDisplay, float> _selectedKeyframesXOffset = new Dictionary<KeyframeDisplay, float>();
        private double _keyframeSelectionSize;
        private int _selectedKeyframeCurvePointIndex = -1;
        private ObjectCtrlInfo _selectedOCI;
        //private GuideObject _selectedGuideObject;
        private readonly AnimationCurve _copiedKeyframeCurve = new AnimationCurve();

        private bool _isAreaSelecting;
        private Vector2 _areaSelectFirstPoint;
        private RectTransform _selectionArea;

#if FEATURE_AUTOGEN
        private bool isAutoGenerating = false;
        private const string AUTOGEN_INTERPOLABLE_FILE = "_interpolable_"; 
#endif
#if FEATURE_SOUND
        private Dictionary<int, Interpolable> _instantActionInterpolables = new Dictionary<int, Interpolable>();
        private KeyValuePair <float, Interpolable> _keepSoundInterpolable = new KeyValuePair<float, Interpolable>(); 
        private readonly Dictionary<int, TimerItem> _activeTimers = new Dictionary<int, TimerItem>();
        private Guid _uuid = Guid.NewGuid();        
#endif
#if FIXED_096
        private Vector2 _cursorPoint;
        private readonly Dictionary<int, ObjectCtrlItem> _ociControlMgmt = new Dictionary<int, ObjectCtrlItem>();
        private Queue<KeyframeDisplay> _keyframeDisplayPool = new Queue<KeyframeDisplay>();
#endif
#if FEATURE_UNDO
        private LimitedStack<TransactionData> _undoStack = new LimitedStack<TransactionData>(20);
        private LimitedStack<TransactionData> _redoStack = new LimitedStack<TransactionData>(20);
#endif
        #endregion

        #region Accessors
        public static float playbackTime { get { return _self._playbackTime; } }
        public static float duration { get { return _self._duration; } }
        public static bool isPlaying
        {
            get { return _self._isPlaying; }
            set
            {
                if (_self._isPlaying != value)
                {
                    _self._isPlaying = value;
                    TimelineButton.UpdateButton();
                }
            }
        }
        #endregion

        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigPlayPauseShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeCopyShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeCutShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframePasteShortcut { get; private set; }
        internal static ConfigEntry<Autoplay> ConfigAutoplay { get; private set; }

#if FEATURE_AUTOGEN
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeSelectAllShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeDeleteAllShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeMoveLeftShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeMoveRightShortcut { get; private set; }       
#endif

#if FEATURE_UNDO
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeUndoShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyframeRedoShortcut { get; private set; }
        internal static ConfigEntry<bool> ConfigKeyEnableUndoRedo { get; private set; }         
#endif

        

        internal enum Autoplay
        {
            Ignore,
            Yes,
            No
        }


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            ConfigMainWindowShortcut = Config.Bind("Config", "Open Timeline UI", new KeyboardShortcut(KeyCode.T, KeyCode.LeftControl));
            ConfigPlayPauseShortcut = Config.Bind("Config", "Play or Pause Timeline", new KeyboardShortcut(KeyCode.T, KeyCode.LeftShift));
            ConfigKeyframeCopyShortcut = Config.Bind("Config", "Copy Keyframes", new KeyboardShortcut(KeyCode.C, KeyCode.LeftControl));
            ConfigKeyframeCutShortcut = Config.Bind("Config", "Cut Keyframes", new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl));
            ConfigKeyframePasteShortcut = Config.Bind("Config", "PasteKeyframes", new KeyboardShortcut(KeyCode.V, KeyCode.LeftControl));
            ConfigAutoplay = Config.Bind("Config", "Autoplay", Autoplay.Ignore);

#if FEATURE_AUTOGEN
            ConfigKeyframeSelectAllShortcut  = Config.Bind("Config", "Select Keyframes all", new KeyboardShortcut(KeyCode.A, KeyCode.LeftControl));
            ConfigKeyframeDeleteAllShortcut  = Config.Bind("Config", "Delete Keyframes all", new KeyboardShortcut(KeyCode.Delete));
            ConfigKeyframeMoveLeftShortcut = Config.Bind("Config", "MoveLeft Keyframes", new KeyboardShortcut(KeyCode.LeftArrow, KeyCode.LeftControl));
            ConfigKeyframeMoveRightShortcut = Config.Bind("Config", "MoveRight Keyframes", new KeyboardShortcut(KeyCode.RightArrow, KeyCode.LeftControl));            
#endif

#if FEATURE_UNDO
            ConfigKeyframeUndoShortcut  = Config.Bind("Config", "Undo", new KeyboardShortcut(KeyCode.U, KeyCode.LeftControl));
            ConfigKeyframeRedoShortcut  = Config.Bind("Config", "Redo", new KeyboardShortcut(KeyCode.Y, KeyCode.LeftControl));
            ConfigKeyEnableUndoRedo = Config.Bind("Enable", $"Undo/Redo", true, "If this is enabled, undo/redo activated"); 
#endif
            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            _singleFilesFolder = Path.Combine(_assemblyLocation, Path.Combine(Name, "Single Files"));

#if HONEYSELECT
            HSExtSave.HSExtSave.RegisterHandler("timeline", null, null, this.SceneLoad, this.SceneImport, this.SceneWrite, null, null);
#else
            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingImported += OnSceneImport;
            ExtensibleSaveFormat.ExtendedSave.SceneBeingSaved += OnSceneSave;
#endif
            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            OCI_OnDelete_Patches.ManualPatch(harmonyInstance);
        }

#if HONEYSELECT
        protected override void LevelLoaded(int level)
        {
            if (level == 3)
                this.Init();
        }
#elif SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }

#elif KOIKATSU
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 1)
                Init();
        }
#endif

        protected override void Update()
        {
            if (_loaded == false)
                return;

            if (Input.anyKeyDown)
            {
                if (ConfigMainWindowShortcut.Value.IsDown())
                {
                    ToggleUiVisible();
                }

                if (ConfigPlayPauseShortcut.Value.IsDown())
                {
                    if (_isPlaying)
                    {
                        Pause();
                    }
                    else
                    {
                        Play();
                    }
                }

                if (_ui.gameObject.activeSelf)
                {
                    if (ConfigKeyframeCopyShortcut.Value.IsDown())
                        CopyKeyframes();
                    else if (ConfigKeyframeCutShortcut.Value.IsDown())
                        CutKeyframes();
                    else if (ConfigKeyframePasteShortcut.Value.IsDown())
                        PasteKeyframes();
#if FEATURE_AUTOGEN
                    else if (ConfigKeyframeSelectAllShortcut.Value.IsDown()) 
                        SelectAllAction();
                    else if (ConfigKeyframeDeleteAllShortcut.Value.IsDown()) 
                        DeleteAllAction();   
                    else if (ConfigKeyframeMoveLeftShortcut.Value.IsDown())
                        MoveLeftKeyframes();
                    else if (ConfigKeyframeMoveRightShortcut.Value.IsDown())
                        MoveRightKeyframes();              
#endif
#if FEATURE_UNDO
                    else if (ConfigKeyframeUndoShortcut.Value.IsDown())
                        UndoPopupAction(0);
                    else if (ConfigKeyframeRedoShortcut.Value.IsDown())
                        UndoPopupAction(1);                 
#endif

                    if (_speedInputField.isFocused == false)
                        _speedInputField.text = Time.timeScale.ToString("0.#####");
                }
            }

            InterpolateBefore();
        }

#if FIXED_096
        private void DelayUpdate() {

            _totalActiveExpressions = _allExpressions.Count(e => e.enabled && e.gameObject.activeInHierarchy);
            _currentExpressionIndex = 0;

            if (_toDelete.Count != 0)
            {
                try
                {
                    RemoveInterpolables(_toDelete);
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to Remove Interpolables from toDelete list: " + ex);
                }
                _toDelete.Clear();
            }

            if (_tooltip.transform.parent.gameObject.activeSelf)
            {
                Vector2 localPoint;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_tooltip.transform.parent.parent, Input.mousePosition, _ui.worldCamera, out localPoint))
                    _tooltip.transform.parent.position = _tooltip.transform.parent.parent.TransformPoint(localPoint);
            }

            TimelineButton.OnUpdate();
        }
#endif

        private void ToggleUiVisible()
        {
            _ui.gameObject.SetActive(!_ui.gameObject.activeSelf);
            if (_ui.gameObject.activeSelf)
                this.ExecuteDelayed2(() =>
                {
                    UpdateInterpolablesView();
                    this.ExecuteDelayed2(
                        () => // I know that's weird but it prevents the grid sometimes disappearing, fuck unity 5.3 I guess
                        {
                            _grid.parent.gameObject.SetActive(false);
                            _grid.parent.gameObject.SetActive(true);
                            LayoutRebuilder.MarkLayoutForRebuild((RectTransform)_grid.parent);
                        }, 4);
                }, 2);
            else
            {
                UIUtility.HideContextMenu();
                TimelineButton.UpdateButton();
            }
        }

        private void PostLateUpdate()
        {
            if (_ui.gameObject.activeSelf && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2)) && UIUtility.IsContextMenuDisplayed() && UIUtility.WasClickInContextMenu() == false)
            {
                UIUtility.HideContextMenu();
                TimelineButton.UpdateButton();
            }

            InterpolateAfter();
        }

#if FIXED_096
        private void DelayCursorUpdate(){
            UpdateCursor();
        }            
#endif        
        #endregion

        #region Public Methods        
        /// <summary>
        /// Start playback or pause it if it's already playing.
        /// </summary>
        public static void Play()
        {
            if (isPlaying == false)
            {
                isPlaying = true;
                _self._startTime = Time.time - _self._playbackTime;

#if FEATURE_SOUND
                _self.SoundCtrl(1);
#endif
#if FIXED_096
                _self.InvokeRepeating(nameof(_self.DelayCursorUpdate), 0f, 0.1f); // 0.2초마다 주기적 호출
#endif
            }
            else
                Pause();
        }

        /// <summary>
        /// Pause playback.
        /// </summary>
        public static void Pause()
        {
            isPlaying = false;
#if FEATURE_SOUND
            _self.SoundCtrl(2);           
#endif
#if FIXED_096
            _self.CancelInvoke(nameof(_self.DelayCursorUpdate));
#endif
        }
        /// <summary>
        /// Stop playback and move cursor to the beginning.
        /// </summary>
        public static void Stop()
        {
            _self._playbackTime = 0f;
            _self.UpdateCursor();
            _self.Interpolate(true);
            _self.Interpolate(false);
            isPlaying = false;
#if FEATURE_SOUND
            _self.SoundCtrl(3);
#endif
#if FIXED_096
            _self.CancelInvoke(nameof(_self.DelayCursorUpdate));
#endif
        }

        /// <summary>
        /// Move playback cursor to the previous frame (based on desired framerate).
        /// </summary>
        public static void PreviousFrame()
        {
            float beat = 1f / _self._desiredFrameRate;
            float time = _self._playbackTime % _self._duration;
            float mod = time % beat;
            if (mod / beat < 0.5f)
                time -= mod;
            else
                time += beat - mod;
            time -= beat;
            if (time < 0f)
                time = 0f;
            _self.SeekPlaybackTime(time);
        }

        /// <summary>
        /// Move playback cursor to the next frame (based on desired framerate).
        /// </summary>
        public static void NextFrame()
        {
            float beat = 1f / _self._desiredFrameRate;
            float time = _self._playbackTime % _self._duration;
            float mod = time % beat;
            if (mod / beat < 0.5f)
                time -= mod;
            else
                time += beat - mod;
            time += beat;
            if (time > _self._duration)
                time = _self._duration;
            _self.SeekPlaybackTime(time);
        }

        /// <summary>
        /// Move playback cursor to the specified time (in seconds).
        /// </summary>
        public static void Seek(float t)
        {
            _self.SeekPlaybackTime(t);
        }


        /// <summary>
        /// Adds an InterpolableModel to the list.
        /// </summary>
        /// <param name="model"></param>
        public static void AddInterpolableModel(InterpolableModel model)
        {
            List<InterpolableModel> models;
            if (_self._interpolableModelsDictionary.TryGetValue(model.owner, out models) == false)
            {
                models = new List<InterpolableModel>();
                _self._interpolableModelsDictionary.Add(model.owner, models);
            }
            models.Add(model);
            _self._interpolableModelsList.Add(model);
        }

        /// <summary>
        /// Adds an InterpolableModel to the list with a constant parameter
        /// </summary>
        public static void AddInterpolableModelStatic(string owner,
                                                      string id,
                                                      object parameter,
                                                      string name,
                                                      InterpolableDelegate interpolateBefore,
                                                      InterpolableDelegate interpolateAfter,
                                                      Func<ObjectCtrlInfo, bool> isCompatibleWithTarget,
                                                      Func<ObjectCtrlInfo, object, object> getValue,
                                                      Func<object, XmlNode, object> readValueFromXml,
                                                      Action<object, XmlTextWriter, object> writeValueToXml,
                                                      Func<ObjectCtrlInfo, XmlNode, object> readParameterFromXml = null,
                                                      Action<ObjectCtrlInfo, XmlTextWriter, object> writeParameterToXml = null,
                                                      Func<ObjectCtrlInfo, object, object, object, bool> checkIntegrity = null,
                                                      bool useOciInHash = true,
                                                      Func<string, ObjectCtrlInfo, object, string> getFinalName = null,
                                                      Func<ObjectCtrlInfo, object, bool> shouldShow = null)
        {
            AddInterpolableModel(new InterpolableModel(owner, id, parameter, name, interpolateBefore, interpolateAfter, isCompatibleWithTarget, getValue, readValueFromXml, writeValueToXml, readParameterFromXml, writeParameterToXml, checkIntegrity, useOciInHash, getFinalName, shouldShow));
        }

        /// <summary>
        /// Adds an interpolableModel to the list with a dynamic parameter
        /// </summary>
        public static void AddInterpolableModelDynamic(string owner,
                                                       string id,
                                                       string name,
                                                       InterpolableDelegate interpolateBefore,
                                                       InterpolableDelegate interpolateAfter,
                                                       Func<ObjectCtrlInfo, bool> isCompatibleWithTarget,
                                                       Func<ObjectCtrlInfo, object, object> getValue,
                                                       Func<object, XmlNode, object> readValueFromXml,
                                                       Action<object, XmlTextWriter, object> writeValueToXml,
                                                       Func<ObjectCtrlInfo, object> getParameter,
                                                       Func<ObjectCtrlInfo, XmlNode, object> readParameterFromXml = null,
                                                       Action<ObjectCtrlInfo, XmlTextWriter, object> writeParameterToXml = null,
                                                       Func<ObjectCtrlInfo, object, object, object, bool> checkIntegrity = null,
                                                       bool useOciInHash = true,
                                                       Func<string, ObjectCtrlInfo, object, string> getFinalName = null,
                                                       Func<ObjectCtrlInfo, object, bool> shouldShow = null)
        {
            AddInterpolableModel(new InterpolableModel(owner, id, name, interpolateBefore, interpolateAfter, isCompatibleWithTarget, getValue, readValueFromXml, writeValueToXml, getParameter, readParameterFromXml, writeParameterToXml, checkIntegrity, useOciInHash, getFinalName, shouldShow));
        }

        /// <summary>
        /// Refreshes the list of displayed interpolables. This function is quite heavy as it must go through each InterpolableModel and check if it's compatible with the current target.
        /// It is called automatically by Timeline when selecting another Workspace object or GuideObject.
        /// </summary>
        public static void RefreshInterpolablesList()
        {
            if (_refreshInterpolablesListScheduled == false)
            {
                _refreshInterpolablesListScheduled = true;
                _self.ExecuteDelayed2(() =>
                {
                    _refreshInterpolablesListScheduled = false;
                    _self.UpdateInterpolablesView();
                });
            }
        }

        /// <summary>
        /// Get all keyframes that are currently selected.
        /// </summary>
        public static IEnumerable<KeyValuePair<float, Keyframe>> GetSelectedKeyframes()
        {
            return _self._selectedKeyframes;
        }

        /// <summary>
        /// Get all keyframes that are in the current project.
        /// </summary>
        /// <param name="onlyEnabled">Only get keyframes of enabled interpolables</param>
        public static IEnumerable<KeyValuePair<float, Keyframe>> GetAllKeyframes(bool onlyEnabled)
        {
            return GetAllInterpolables(onlyEnabled).SelectMany(x => x.keyframes);
        }

        /// <summary>
        /// Get all interpolables that are in the current project.
        /// </summary>
        /// <param name="onlyEnabled">Only get enabled interpolables</param>
        public static IEnumerable<Interpolable> GetAllInterpolables(bool onlyEnabled)
        {
            return onlyEnabled ? _self._interpolables.Values.Where(x => x.enabled) : _self._interpolables.Values;
        }

        /// <summary>
        /// Is the main timeline window visible?
        /// </summary>
        public static bool InterfaceVisible
        {
            get
            {
                return _self._ui.gameObject.activeSelf;
            }
            set
            {
                if (_self._ui.gameObject.activeSelf != value)
                    _self.ToggleUiVisible();
            }
        }

        /// <summary>
        /// Get an estimation of the real duration of the entire timeline, accounting for time scale changes.
        /// Calculation cost is not trivial (expect ~1ms execution cost for 50 seconds of timeline).
        /// </summary>
        public static float EstimateRealDuration()
        {
            float realDuration = 0;
            Interpolable interpolable = _self._interpolables.Values.FirstOrDefault(x => x.id == "timeScale");
            if (interpolable == null)
                return (Time.timeScale == 0) ? duration : duration / Time.timeScale;

            List<KeyValuePair<float, Keyframe>> keyframes = interpolable.keyframes.TakeWhile(x => x.Key <= duration).ToList();
            if (keyframes.Count == 0)
                return (Time.timeScale == 0) ? duration : duration / Time.timeScale;

            // In the interval [0, firstKeyframe], Timeline uses the value of the first keyframe
            realDuration += keyframes.First().Key / (float)keyframes.First().Value.value;

            KeyValuePair<float, Keyframe> keyframeAfterEnd = interpolable.keyframes.FirstOrDefault(x => x.Key > duration);
            if (!keyframeAfterEnd.Equals(default(KeyValuePair<float, Keyframe>)))
            {
                // In the interval [lastKeyframe, duration], Timeline still interpolates if there is a keyframe outside of the duration window
                KeyValuePair<float, Keyframe> lastKeyframe = keyframes.Last();
                float normalizedTime = (duration - lastKeyframe.Key) / (keyframeAfterEnd.Key - lastKeyframe.Key);
                float normalizedValue = keyframeAfterEnd.Value.curve.Evaluate(normalizedTime);
                float valueAtEnd = (float)lastKeyframe.Value.value + normalizedValue * ((float)keyframeAfterEnd.Value.value - (float)lastKeyframe.Value.value);
                realDuration += IntegrateTimescaleReciprocal(keyframeAfterEnd.Value.curve, (float)lastKeyframe.Value.value, valueAtEnd, duration - lastKeyframe.Key);
            }
            else
            {
                // In the interval [lastKeyframe, duration], Timeline uses the value of the last keyframe
                realDuration += (duration - keyframes.Last().Key) / (float)keyframes.Last().Value.value;
            }

            for (int i = 0; i < keyframes.Count - 1; i++)
            {
                KeyValuePair<float, Keyframe> current = keyframes.ElementAt(i);
                KeyValuePair<float, Keyframe> next = keyframes.ElementAt(i + 1);
                float value = IntegrateTimescaleReciprocal(current.Value.curve, (float)current.Value.value, (float)next.Value.value, next.Key - current.Key);
                realDuration += value;
            }
            return realDuration;
        }

        public static RectTransform MainWindowRectTransform => _self._timelineWindow;
        #endregion

        #region Private Methods

#if FEATURE_SOUND
        // type = 1(play), type = 2(pause), type =3(stop)
        private void SoundCtrl(int type) {
            foreach (KeyValuePair<int, ObjectCtrlInfo> pair in Studio.Studio.Instance.dicObjectCtrl)
            {
                AudioSource audioSource = pair.Value.guideObject.gameObject.GetComponent<AudioSource>();
                if (audioSource != null) {
                    if (type == 1) {
                        if (audioSource.loop) {
                            audioSource.Play();
                        }
                    } else if (type == 2) {
                        if(audioSource.loop) {
                            audioSource.Pause();
                        }
                    } else {
                        audioSource.Stop();
                        UnityEngine.Object.Destroy(audioSource);
                    }
                }
            }

            if (type == 1) {
                if (_self._activeTimers.Count == 0) {
                    _self.RegisterSoundTimer(_self._playbackTime);
                }
            } else {
                _self.UnregisterSoundTimers();   
            }
        }
#endif

#if FIXED_096
        private IEnumerator CreateKeyFrameDisplaysCoroutine(int countPerFrame, int totalCount)
        {
            for (int i = 0; i < totalCount; i++)
            {
                _keyframeDisplayPool.Enqueue(CreateKeyFrameDisplay());

                // 일정 수 처리 후 프레임 넘김
                if ((i + 1) % countPerFrame == 0)
                    yield return null;
            }

#if FIXED_096_DEBUG
            // 기본 keyframeGroup 생성
            ObjectCtrlItem objectCtrlItem = new ObjectCtrlItem();

            GameObject keyframeGroup = new GameObject($"0");
            keyframeGroup.transform.SetParent(_keyframesContainer.gameObject.transform, false);
            keyframeGroup.transform.localPosition = Vector3.zero;
            objectCtrlItem.keyframeGroup = keyframeGroup;    
            objectCtrlItem.displayedKeyframes = new List<KeyframeDisplay>();
            objectCtrlItem.oci = null;
            objectCtrlItem.dirty = true;

            _ociControlMgmt.Add(0, objectCtrlItem);
#endif
            UpdateInterpolablesView();
        }
        // keyframeDisplay 오브젝트 꺼내기
        public KeyframeDisplay GetFromKeyframeDisplayPool()
        {
            if (_keyframeDisplayPool.Count > 0)
            {
                KeyframeDisplay obj = _keyframeDisplayPool.Dequeue();
                return obj;
            }
            else
            {
                KeyframeDisplay obj =  CreateKeyFrameDisplay();
                return obj;
            }
        }

        // keyframeDisplay 오브젝트 반납
        public void ReturnToKeyframeDisplayPool(KeyframeDisplay obj)
        {
            obj.gameObject.SetActive(false);
            obj.keyframe = null;
            obj.gameObject.transform.SetParent(null); // 풀로 귀환
            _keyframeDisplayPool.Enqueue(obj);
        }
#endif

        private Interpolable AddInterpolable(InterpolableModel model)
        {
            bool added = false;
            Interpolable actualInterpolable = null;
            try
            {
                if (model.IsCompatibleWithTarget(_selectedOCI) == false)
                    return null;
                Interpolable interpolable = new Interpolable(_selectedOCI, model);

                if (_interpolables.TryGetValue(interpolable.GetHashCode(), out actualInterpolable) == false)
                {
                    _interpolables.Add(interpolable.GetHashCode(), interpolable);
                    _interpolablesTree.AddLeaf(interpolable);
#if FEATURE_SOUND
                    if(interpolable.instantAction) {
                        if (!_instantActionInterpolables.ContainsKey(interpolable.oci.GetHashCode())) {
                            _instantActionInterpolables.Add(interpolable.oci.GetHashCode(), interpolable);
                        }
                    }
#endif
                    actualInterpolable = interpolable;
                    added = true;
                }
                UpdateInterpolablesView();
                return actualInterpolable;
            }
            catch (Exception e)
            {
                Logger.LogError("Couldn't add interpolable with model:\n" + model + "\n" + e);
                if (added)
                {
                    _interpolables.Remove(actualInterpolable.GetHashCode());
                    _interpolablesTree.RemoveLeaf(actualInterpolable);
                    UpdateInterpolablesView();
                }
            }
            return null;
        }

#if FEATURE_UNDO
        private void RemoveInterpolableUndo(Interpolable interpolable)
        {
            _interpolables.Remove(interpolable.GetHashCode());
            _interpolablesTree.RemoveLeaf(interpolable);
        }
#endif

        private void RemoveInterpolable(Interpolable interpolable)
        {
            _interpolables.Remove(interpolable.GetHashCode());
            int selectedIndex = _selectedInterpolables.IndexOf(interpolable);
            if (selectedIndex != -1)
                _selectedInterpolables.RemoveAt(selectedIndex);
            _interpolablesTree.RemoveLeaf(interpolable);
            _selectedKeyframes.RemoveAll(elem => elem.Value.parent == interpolable);
#if FIXED_096_DEBUG
            SetObjectCtrlDirty(_selectedOCI);
#endif
            UpdateInterpolablesView();
            UpdateKeyframeWindow(false);
        }

        private void RemoveInterpolables(IEnumerable<Interpolable> interpolables)
        {
            if (interpolables == _selectedInterpolables)
                interpolables = interpolables.ToArray();

            foreach (Interpolable interpolable in interpolables)
            {
                if (_interpolables.ContainsKey(interpolable.GetHashCode()))
                    _interpolables.Remove(interpolable.GetHashCode());
                _interpolablesTree.RemoveLeaf(interpolable);

                int index = _selectedInterpolables.IndexOf(interpolable);
                if (index != -1)
                    _selectedInterpolables.RemoveAt(index);
                _selectedKeyframes.RemoveAll(elem => elem.Value.parent == interpolable);
#if FEATURE_AUTOGEN
                interpolable.keyframes.Clear();
#endif
            }
#if FIXED_096_DEBUG
            SetObjectCtrlDirty(_selectedOCI);
#endif
            UpdateInterpolablesView();
            UpdateKeyframeWindow(false);
        }

        private void Init()
        {
            UIUtility.Init();

            BuiltInInterpolables.Populate();

            if (Camera.main.GetComponent<Expression>() == null)
                Camera.main.gameObject.AddComponent<Expression>();
            _allGuideObjects = (Dictionary<Transform, GuideObject>)GuideObjectManager.Instance.GetPrivate("dicGuideObject");
            _selectedGuideObjects = (HashSet<GuideObject>)GuideObjectManager.Instance.GetPrivate("hashSelectObject");
#if HONEYSELECT
            AssetBundle bundle = AssetBundle.LoadFromMemory(Assembly.GetExecutingAssembly().GetResource("Timeline.Resources.TimelineResources.unity3d"));
#elif KOIKATSU || AISHOUJO || HONEYSELECT2
            AssetBundle bundle = AssetBundle.LoadFromMemory(Assembly.GetExecutingAssembly().GetResource("Timeline.Resources.TimelineResourcesKoi.unity3d"));
#endif
            GameObject uiPrefab = bundle.LoadAsset<GameObject>("Canvas");
            _ui = GameObject.Instantiate(uiPrefab).GetComponent<Canvas>();
            CanvasGroup alphaGroup = _ui.GetComponent<CanvasGroup>();
            uiPrefab.hideFlags |= HideFlags.HideInHierarchy;
            _keyframePrefab = bundle.LoadAsset<GameObject>("Keyframe");
            _keyframePrefab.hideFlags |= HideFlags.HideInHierarchy;
            _keyframesBackgroundMaterial = bundle.LoadAsset<Material>("KeyframesBackground");
            _interpolablePrefab = bundle.LoadAsset<GameObject>("Interpolable");
            _interpolablePrefab.hideFlags |= HideFlags.HideInHierarchy;
            _interpolableModelPrefab = bundle.LoadAsset<GameObject>("InterpolableModel");
            _interpolableModelPrefab.hideFlags |= HideFlags.HideInHierarchy;
            _curveKeyframePrefab = bundle.LoadAsset<GameObject>("CurveKeyframe");
            _curveKeyframePrefab.hideFlags |= HideFlags.HideInHierarchy;
            _headerPrefab = bundle.LoadAsset<GameObject>("Header");
            _headerPrefab.hideFlags |= HideFlags.HideInHierarchy;
            _singleFilePrefab = bundle.LoadAsset<GameObject>("SingleFile");
            _singleFilePrefab.hideFlags |= HideFlags.HideInHierarchy;

            _ui.transform.Find("Timeline Window/Help Panel/Main Container/Scroll View/Viewport/Content/Text").GetComponent<Text>().text = System.Text.Encoding.Default.GetString(Assembly.GetExecutingAssembly().GetResource("Timeline.Resources.Help.txt"));

            foreach (Sprite sprite in bundle.LoadAllAssets<Sprite>())
            {
                switch (sprite.name)
                {
                    case "Link":
                        _linkSprite = sprite;
                        break;
                    case "Color":
                        _colorSprite = sprite;
                        break;
                    case "Rename":
                        _renameSprite = sprite;
                        break;
                    case "NewFolder":
                        _newFolderSprite = sprite;
                        break;
                    case "Add":
                        _addSprite = sprite;
                        break;
                    case "AddToFolder":
                        _addToFolderSprite = sprite;
                        break;
                    case "ChevronUp":
                        _chevronUpSprite = sprite;
                        break;
                    case "ChevronDown":
                        _chevronDownSprite = sprite;
                        break;
                    case "Delete":
                        _deleteSprite = sprite;
                        break;
                    case "Checkbox":
                        _checkboxSprite = sprite;
                        break;
                    case "CheckboxComposite":
                        _checkboxCompositeSprite = sprite;
                        break;
                    case "SelectAll":
                        _selectAllSprite = sprite;
                        break;
                }
            }

            bundle.Unload(false);

            _tooltip = _ui.transform.Find("Tooltip/Text").GetComponent<Text>();

            //Timeline window
            _timelineWindow = (RectTransform)_ui.transform.Find("Timeline Window");
            UIUtility.MakeObjectDraggable((RectTransform)_ui.transform.Find("Timeline Window/Top Container"), _timelineWindow, (RectTransform)_ui.transform);
            _helpPanel = _ui.transform.Find("Timeline Window/Help Panel").gameObject;
            _singleFilesPanel = _ui.transform.Find("Timeline Window/Single Files Panel").gameObject;
            _singleFilesContainer = (RectTransform)_singleFilesPanel.transform.Find("Main Container/Scroll View/Viewport/Content");
            _singleFileNameField = _singleFilesPanel.transform.Find("Main Container/Buttons/Name").GetComponent<InputField>();
            _verticalScrollView = _ui.transform.Find("Timeline Window/Main Container/Timeline/Interpolables").GetComponent<ScrollRect>();
            _horizontalScrollView = _ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View").GetComponent<ScrollRect>();
            _allToggle = _ui.transform.Find("Timeline Window/Main Container/Timeline/Interpolables/Top/All").GetComponent<Toggle>();
            _interpolablesSearchField = _ui.transform.Find("Timeline Window/Main Container/Search").GetComponent<InputField>();
            _interpolablesSearchRegex = new Regex(".*", RegexOptions.IgnoreCase);
            _grid = (RectTransform)_ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container");
            _gridImage = _ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container/Grid/Viewport/Background").GetComponent<RawImage>();
            _gridImage.material = new Material(_gridImage.material);
            _gridTop = (RectTransform)_ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container/Texts/Background");
            _cursor = (RectTransform)_ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container/Cursor");
            _frameRateInputField = _ui.transform.Find("Timeline Window/Buttons/Play Buttons/FrameRate").GetComponent<InputField>();
            _timeInputField = _ui.transform.Find("Timeline Window/Buttons/Time").GetComponent<InputField>();
            _blockLengthInputField = _ui.transform.Find("Timeline Window/Buttons/Block Divisions/Block Length").GetComponent<InputField>();
            _divisionsInputField = _ui.transform.Find("Timeline Window/Buttons/Block Divisions/Divisions").GetComponent<InputField>();
            _durationInputField = _ui.transform.Find("Timeline Window/Buttons/Duration").GetComponent<InputField>();
            _speedInputField = _ui.transform.Find("Timeline Window/Buttons/Speed").GetComponent<InputField>();
            _textsContainer = (RectTransform)_ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container/Texts");
            _keyframesContainer = (RectTransform)_ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container/Grid/Viewport/Content");
            _selectionArea = (RectTransform)_ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container/Grid/Viewport/Content/Selection");
            _miscContainer = (RectTransform)_ui.transform.Find("Timeline Window/Main Container/Timeline/Scroll View/Viewport/Content/Grid Container/Grid/Viewport/Misc Content");
            _resizeHandle = (RectTransform)_ui.transform.Find("Timeline Window/Resize Handle");

#if SUNSHINE
            // The input text is not visible when typing in Sunshine. So change the color.
            var colors = _interpolablesSearchField.colors;
            colors.selectedColor = colors.normalColor * 0.75f;
            _interpolablesSearchField.colors = colors;
#endif

            _ui.transform.Find("Timeline Window/Buttons/Play Buttons/Play").GetComponent<Button>().onClick.AddListener(Play);
            _ui.transform.Find("Timeline Window/Buttons/Play Buttons/Pause").GetComponent<Button>().onClick.AddListener(Pause);
            _ui.transform.Find("Timeline Window/Buttons/Play Buttons/Stop").GetComponent<Button>().onClick.AddListener(Stop);
            _ui.transform.Find("Timeline Window/Buttons/Play Buttons/PrevFrame").GetComponent<Button>().onClick.AddListener(PreviousFrame);
            _ui.transform.Find("Timeline Window/Buttons/Play Buttons/NextFrame").GetComponent<Button>().onClick.AddListener(NextFrame);
            _ui.transform.Find("Timeline Window/Buttons/Single Files").GetComponent<Button>().onClick.AddListener(ToggleSingleFilesPanel);
            _singleFileNameField.onValueChanged.AddListener((s) => UpdateSingleFileSelection());
            _singleFilesPanel.transform.Find("Main Container/Buttons/Load").GetComponent<Button>().onClick.AddListener(LoadSingleFile);
            _singleFilesPanel.transform.Find("Main Container/Buttons/Save").GetComponent<Button>().onClick.AddListener(SaveSingleFile);
            _singleFilesPanel.transform.Find("Main Container/Buttons/Delete").GetComponent<Button>().onClick.AddListener(DeleteSingleFile);
            _ui.transform.Find("Timeline Window/Buttons/Help").GetComponent<Button>().onClick.AddListener(ToggleHelp);

            _frameRateInputField.onEndEdit.AddListener(UpdateDesiredFrameRate);
            _timeInputField.onEndEdit.AddListener(UpdatePlaybackTime);
            _durationInputField.onEndEdit.AddListener(UpdateDuration);
            _blockLengthInputField.onEndEdit.AddListener(UpdateBlockLength);
            _blockLengthInputField.text = _blockLength.ToString();
            _divisionsInputField.onEndEdit.AddListener(UpdateDivisions);
            _divisionsInputField.text = _divisions.ToString();
            _speedInputField.onEndEdit.AddListener(UpdateSpeed);
            _keyframesContainer.gameObject.AddComponent<PointerDownHandler>().onPointerDown = OnKeyframeContainerMouseDown;
            _gridTop.gameObject.AddComponent<PointerDownHandler>().onPointerDown = OnGridTopMouse;
            _ui.transform.Find("Timeline Window/Top Container").gameObject.AddComponent<ScrollHandler>().onScroll = e =>
            {
                if (Input.GetKey(KeyCode.LeftControl))
                {
                    if (e.scrollDelta.y > 0)
                        alphaGroup.alpha = Mathf.Min(alphaGroup.alpha + 0.05f, 1f);
                    else
                        alphaGroup.alpha = Mathf.Max(alphaGroup.alpha - 0.05f, 0.1f);
                    e.Reset();
                }
                else
                {
                    if (e.scrollDelta.y > 0)
                        interpolableHeight = Mathf.Min(interpolableHeight + 1, _interpolableMaxHeight);
                    else
                        interpolableHeight = Mathf.Max(interpolableHeight - 1, _interpolableMinHeight);

                    UpdateInterpolablesView();
                }
            };
            DragHandler handler = _gridTop.gameObject.AddComponent<DragHandler>();
            //handler.onBeginDrag = (e) =>
            //{
            //    this.OnGridTopMouse(e);
            //    e.Reset();
            //};
            handler.onDrag = (e) =>
            {
                isPlaying = false;
                _isDraggingCursor = true;
                OnGridTopMouse(e);
                e.Reset();
            };
            handler.onEndDrag = (e) =>
            {
                _isDraggingCursor = false;
                OnGridTopMouse(e);
                e.Reset();
            };
            _gridTop.gameObject.AddComponent<ScrollHandler>().onScroll = e =>
            {
                if (e.scrollDelta.y > 0)
                    ZoomIn();
                else
                    ZoomOut();
                e.Reset();
            };
            _verticalScrollView.onValueChanged.AddListener(ScrollVerticalKeyframes);            
            _keyframesContainer.gameObject.AddComponent<ScrollHandler>().onScroll = e =>
            {
                if (Input.GetKey(KeyCode.LeftControl))
                {
                    if (e.scrollDelta.y > 0)
                        ZoomIn();
                    else
                        ZoomOut();
                    e.Reset();
                }
                else if (Input.GetKey(KeyCode.LeftAlt))
                {
                    ScaleKeyframeSelection(e.scrollDelta.y);
                    e.Reset();
                }
                else if (Input.GetKey(KeyCode.LeftShift) == false)
                {
                    _verticalScrollView.OnScroll(e);
                    e.Reset();
                }
                else
                {
                    _horizontalScrollView.OnScroll(e);
                    e.Reset();
                }
            };

            handler = _keyframesContainer.gameObject.AddComponent<DragHandler>();
            handler.onInitializePotentialDrag = (e) =>
            {
                PotentiallyBeginAreaSelect(e);
                e.Reset();
            };
            handler.onBeginDrag = (e) =>
            {
                BeginAreaSelect(e);
                e.Reset();
            };
            handler.onDrag = (e) =>
            {
                UpdateAreaSelect(e);
                e.Reset();
            };
            handler.onEndDrag = (e) =>
            {
                EndAreaSelect(e);
                e.Reset();
            };
            _allToggle.onValueChanged.AddListener(b => UpdateInterpolablesView());
            _interpolablesSearchField.onValueChanged.AddListener(InterpolablesSearch);
            handler = _resizeHandle.gameObject.AddComponent<DragHandler>();
            handler.onDrag = OnResizeWindow;

            //Keyframe window
            _keyframeWindow = _ui.transform.Find("Keyframe Window").gameObject;
            UIUtility.MakeObjectDraggable((RectTransform)_keyframeWindow.transform.Find("Top Container"), (RectTransform)_keyframeWindow.transform, (RectTransform)_ui.transform);
            // 성능 개선
            var mainFields = _keyframeWindow.transform.Find("Main Container/Main Fields");
            _keyframeInterpolableNameText = mainFields.Find("Interpolable Name").GetComponent<Text>();
            _keyframeSelectPrevButton = mainFields.Find("Prev Next/Prev").GetComponent<Button>();
            _keyframeSelectNextButton = mainFields.Find("Prev Next/Next").GetComponent<Button>();
            _keyframeTimeTextField = mainFields.Find("Time/InputField").GetComponent<InputField>();
            _keyframeUseCurrentTimeButton = mainFields.Find("Use Current Time").GetComponent<Button>();
            _keyframeValueText = mainFields.Find("Value/Background/Text").GetComponent<Text>();
            _keyframeUseCurrentValueButton = mainFields.Find("Use Current").GetComponent<Button>();
            Button deleteButton = mainFields.Find("Delete").GetComponent<Button>();

            // _keyframeInterpolableNameText = _keyframeWindow.transform.Find("Main Container/Main Fields/Interpolable Name").GetComponent<Text>();
            // _keyframeSelectPrevButton = _keyframeWindow.transform.Find("Main Container/Main Fields/Prev Next/Prev").GetComponent<Button>();
            // _keyframeSelectNextButton = _keyframeWindow.transform.Find("Main Container/Main Fields/Prev Next/Next").GetComponent<Button>();
            // _keyframeTimeTextField = _keyframeWindow.transform.Find("Main Container/Main Fields/Time/InputField").GetComponent<InputField>();
            // _keyframeUseCurrentTimeButton = _keyframeWindow.transform.Find("Main Container/Main Fields/Use Current Time").GetComponent<Button>();
            // _keyframeValueText = _keyframeWindow.transform.Find("Main Container/Main Fields/Value/Background/Text").GetComponent<Text>();
            // _keyframeUseCurrentValueButton = _keyframeWindow.transform.Find("Main Container/Main Fields/Use Current").GetComponent<Button>();
            // Button deleteButton = _keyframeWindow.transform.Find("Main Container/Main Fields/Delete").GetComponent<Button>();
            _keyframeDeleteButtonText = deleteButton.GetComponentInChildren<Text>();

            _curveContainer = _keyframeWindow.transform.Find("Main Container/Curve Fields/Curve/Grid/Spline").GetComponent<RawImage>();
            _curveContainer.material = new Material(_curveContainer.material);
            _curveTimeInputField = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point Time/InputField").GetComponent<InputField>();
            _curveTimeSlider = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point Time/Slider").GetComponent<Slider>();
            _curveValueInputField = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point Value/InputField").GetComponent<InputField>();
            _curveValueSlider = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point Value/Slider").GetComponent<Slider>();
            _curveInTangentInputField = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point InTangent/InputField").GetComponent<InputField>();
            _curveInTangentSlider = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point InTangent/Slider").GetComponent<Slider>();
            _curveOutTangentInputField = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point OutTangent/InputField").GetComponent<InputField>();
            _curveOutTangentSlider = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Curve Point OutTangent/Slider").GetComponent<Slider>();
            _cursor2 = (RectTransform)_ui.transform.Find("Keyframe Window/Main Container/Curve Fields/Curve/Grid/Cursor");

            _keyframeWindow.transform.Find("Close").GetComponent<Button>().onClick.AddListener(CloseKeyframeWindow);
            _keyframeSelectPrevButton.onClick.AddListener(SelectPreviousKeyframe);
            _keyframeSelectNextButton.onClick.AddListener(SelectNextKeyframe);
            _keyframeUseCurrentTimeButton.onClick.AddListener(UseCurrentTime);
            _keyframeWindow.transform.Find("Main Container/Main Fields/Drag At Current Time").GetComponent<Button>().onClick.AddListener(DragAtCurrentTime);
            _keyframeUseCurrentValueButton.onClick.AddListener(UseCurrentValue);
            deleteButton.onClick.AddListener(DeleteSelectedKeyframes);

            // 성능 개선
            Transform presets = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Presets");
            Transform buttons = _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Buttons");

            Button _btnLine = presets.Find("Line").GetComponent<Button>();
            Button _btnTop = presets.Find("Top").GetComponent<Button>();
            Button _btnBottom = presets.Find("Bottom").GetComponent<Button>();
            Button _btnHermite = presets.Find("Hermite").GetComponent<Button>();
            Button _btnStairs = presets.Find("Stairs").GetComponent<Button>();
            Button _btnCopy = buttons.Find("Copy").GetComponent<Button>();
            Button _btnPaste = buttons.Find("Paste").GetComponent<Button>();
            Button _btnInvert = buttons.Find("Invert").GetComponent<Button>();

            // 연결
            _btnLine.onClick.AddListener(() => ApplyKeyframeCurvePreset(_linePreset));
            _btnTop.onClick.AddListener(() => ApplyKeyframeCurvePreset(_topPreset));
            _btnBottom.onClick.AddListener(() => ApplyKeyframeCurvePreset(_bottomPreset));
            _btnHermite.onClick.AddListener(() => ApplyKeyframeCurvePreset(_hermitePreset));
            _btnStairs.onClick.AddListener(() => ApplyKeyframeCurvePreset(_stairsPreset));

            _btnCopy.onClick.AddListener(CopyKeyframeCurve);
            _btnPaste.onClick.AddListener(PasteKeyframeCurve);
            _btnInvert.onClick.AddListener(InvertKeyframeCurve);

            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Presets/Line").GetComponent<Button>().onClick.AddListener(() => ApplyKeyframeCurvePreset(_linePreset));
            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Presets/Top").GetComponent<Button>().onClick.AddListener(() => ApplyKeyframeCurvePreset(_topPreset));
            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Presets/Bottom").GetComponent<Button>().onClick.AddListener(() => ApplyKeyframeCurvePreset(_bottomPreset));
            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Presets/Hermite").GetComponent<Button>().onClick.AddListener(() => ApplyKeyframeCurvePreset(_hermitePreset));
            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Presets/Stairs").GetComponent<Button>().onClick.AddListener(() => ApplyKeyframeCurvePreset(_stairsPreset));
            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Buttons/Copy").GetComponent<Button>().onClick.AddListener(CopyKeyframeCurve);
            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Buttons/Paste").GetComponent<Button>().onClick.AddListener(PasteKeyframeCurve);
            // _keyframeWindow.transform.Find("Main Container/Curve Fields/Fields/Buttons/Invert").GetComponent<Button>().onClick.AddListener(InvertKeyframeCurve);

            _keyframeTimeTextField.onEndEdit.AddListener(UpdateSelectedKeyframeTime);

            _curveContainer.gameObject.AddComponent<PointerDownHandler>().onPointerDown = OnCurveMouseDown;
            _curveTimeInputField.onEndEdit.AddListener(UpdateCurvePointTime);
            _curveTimeSlider.onValueChanged.AddListener(UpdateCurvePointTime);
            _curveValueInputField.onEndEdit.AddListener(UpdateCurvePointValue);
            _curveValueSlider.onValueChanged.AddListener(UpdateCurvePointValue);
            _curveInTangentInputField.onEndEdit.AddListener(UpdateCurvePointInTangent);
            _curveInTangentSlider.onValueChanged.AddListener(UpdateCurvePointInTangent);
            _curveOutTangentInputField.onEndEdit.AddListener(UpdateCurvePointOutTangent);
            _curveOutTangentSlider.onValueChanged.AddListener(UpdateCurvePointOutTangent);
            _ui.gameObject.SetActive(false);
            _helpPanel.gameObject.SetActive(false);
            _singleFilesPanel.gameObject.SetActive(false);
            _keyframeWindow.gameObject.SetActive(false);
            _tooltip.transform.parent.gameObject.SetActive(false);

#if FIXED_096
            StartCoroutine(CreateKeyFrameDisplaysCoroutine(
                countPerFrame: 1000,     // 1프레임당 최대 1000개 처리
                totalCount: 30000       // 총 생성할 KeyframeDisplay 수
            ));

            InvokeRepeating(nameof(DelayUpdate), 0f, 1f); // 1초마다 주기적 호출
#endif
            _loaded = true;

            // Wrap in a try since it will crash if KKAPI is not installed
            try { StartCoroutine(TimelineButton.Init()); }
            catch (Exception ex) { Logger.LogError(ex); }
        }

        private void ScrollVerticalKeyframes(Vector2 arg0)
        {
            _keyframesContainer.anchoredPosition = new Vector2(_keyframesContainer.anchoredPosition.x, _verticalScrollView.content.anchoredPosition.y);
            _miscContainer.anchoredPosition = new Vector2(_miscContainer.anchoredPosition.x, _verticalScrollView.content.anchoredPosition.y);
        }
        // private void ScrollHorizontalKeyframes(Vector2 arg0)
        // {
        //     // _keyframesContainer.anchoredPosition = new Vector2(_horizontalScrollView.content.anchoredPosition.x, _keyframesContainer.anchoredPosition.y);
        // }

        private void InterpolateBefore()
        {
            if (_isPlaying)
            {
#if FEATURE_SOUND
                float _curTime = (Time.time - _startTime) % _duration;
                if (_curTime < _playbackTime) {
                    UnregisterSoundTimers();
                    RegisterSoundTimer(_curTime);
                }
               
                _playbackTime = _curTime;
#else
                _playbackTime = (Time.time - _startTime) % _duration;
#endif
                // UpdateCursor();
                Interpolate(true);
            }
        }

        private void InterpolateAfter()
        {
            if (_isPlaying)
            {
                Interpolate(false);
            }
        }

#if FEATURE_SOUND
        private void RegisterSoundTimer(float startPlayTime){

            foreach (KeyValuePair<int, Interpolable> pair in _instantActionInterpolables) {

                AudioSource audioSource = pair.Value.oci.guideObject.gameObject.GetComponent<AudioSource>();

                if (audioSource == null) {
                    pair.Value.oci.guideObject.gameObject.AddComponent<AudioSource>();                                                
                }

                foreach (KeyValuePair<float, Keyframe> keyframePair in pair.Value.keyframes)
                {
                    float time = Math.Max(keyframePair.Key - startPlayTime, 0);
                    if (time > 0.0f) {
                        ScheduleCall(pair.Value,  time, keyframePair.Value);
                    }
                }
            }
        }

        private void UnregisterSoundTimers(){

            foreach (TimerItem timerItem in _activeTimers.Values)
            {
                StopCoroutine(timerItem.coroutine);
                if(timerItem.interpolable.id == "SoundBGControl") {
                    AudioSource audioSource = timerItem.interpolable.oci.guideObject.gameObject.GetComponent<AudioSource>();
                     if (audioSource != null) {
                        audioSource.Stop();
                     }
                }
            }
            _activeTimers.Clear();       
        }

        // 등록: delay 후 Call() 실행, 고유 timerId 반환
        public int ScheduleCall(Interpolable interpolable, float delayInSeconds, Keyframe keyframe)
        {  
            int id = _activeTimers.Count + 1;
            TimerItem timerItem = new TimerItem();
            timerItem.interpolable = interpolable;
            timerItem.coroutine = StartCoroutine(CallAfterDelay(id, interpolable, delayInSeconds, keyframe));

            _activeTimers.Add(id, timerItem);

            return id;
        }

        // 타이머 실행 로직
        private IEnumerator CallAfterDelay(int timerId, Interpolable interpolable, float delay, Keyframe keyframe)
        {
            yield return new WaitForSeconds(delay);
            if (_activeTimers.ContainsKey(timerId))
            {
                interpolable.InterpolateBefore(keyframe.value, 0, 0);
            }
        }
#endif

#if FEATURE_SOUND
        private Dictionary<string, SoundItem> SearchActiveSound()
        {
            Dictionary<string, SoundItem> activeSoundFiles = new Dictionary<string, SoundItem>();

            foreach (KeyValuePair<int, Interpolable> pair in _instantActionInterpolables) { 
                foreach (KeyValuePair<float, Keyframe> keyframePair in pair.Value.keyframes)
                {
                    string  value =  keyframePair.Value.value?.ToString() ?? string.Empty;
                    if (value != string.Empty) {
                        Timeline.SoundItem SoundItem = JsonUtility.FromJson<Timeline.SoundItem>(value);
                        
                        if (!activeSoundFiles.ContainsKey(SoundItem.fileName)) {
                            activeSoundFiles.Add(SoundItem.fileName, SoundItem);
                        }
                    }
                }
            }

            return activeSoundFiles;
        }
#endif
        private void Interpolate(bool before)
        {
            KeyValuePair<float, Keyframe> left = default;
            KeyValuePair<float, Keyframe> right = default;

            _interpolablesTree.Recurse((node, depth) =>
            {
                if (node.type != INodeType.Leaf)
                    return;
                Interpolable interpolable = ((LeafNode<Interpolable>)node).obj;
                if (interpolable.enabled == false)
                {
#if FEATURE_SOUND
                    if (interpolable.instantAction) {
                        AudioSource audioSource = interpolable.oci.guideObject.gameObject.GetComponent<AudioSource>();

                        if (audioSource != null) {
                            audioSource.mute = true;
                        }
                    }
#endif
                    return;
                }
#if FEATURE_AUTOGEN
                if (interpolable.keyframes.Count == 1) {
                    KeyValuePair<float, Keyframe> keyframePair = interpolable.keyframes.ToList()[0];
                    if (Math.Round(keyframePair.Key, 3) == 0.099f)
                    {
                        return;
                    }
                }
#endif
                if (before)
                {
                    if (interpolable.canInterpolateBefore == false)
                        return;
                }
                else
                {
                    if (interpolable.canInterpolateAfter == false)
                        return;
                }
#if FEATURE_SOUND
                if (interpolable.instantAction)
                {
                    AudioSource audioSource = interpolable.oci.guideObject.gameObject.GetComponent<AudioSource>();
                    if (audioSource != null) {
                        audioSource.mute = false;
                    }
                } else {                                       
                    foreach (KeyValuePair<float, Keyframe> keyframePair in interpolable.keyframes)
                    {
                        if (keyframePair.Key <= _playbackTime)
                            left = keyframePair;
                        else
                        {
                            right = keyframePair;
                            break;
                        }
                    }                    
                }
#else
                foreach (KeyValuePair<float, Keyframe> keyframePair in interpolable.keyframes)
                {
                    if (keyframePair.Key <= _playbackTime)
                        left = keyframePair;
                    else
                    {
                        right = keyframePair;
                        break;
                    }
                }
#endif
                bool res = true;

                if (left.Value != null && right.Value != null)
                {
                    float normalizedTime = (_playbackTime - left.Key) / (right.Key - left.Key);
                    normalizedTime = left.Value.curve.Evaluate(normalizedTime);
                    if (before)
                        res = interpolable.InterpolateBefore(left.Value.value, right.Value.value, normalizedTime);
                    else
                        res = interpolable.InterpolateAfter(left.Value.value, right.Value.value, normalizedTime);

                    left = default;
                    right = default;
                }
                else if (left.Value != null)
                {
                    if (before)
                        res = interpolable.InterpolateBefore(left.Value.value, left.Value.value, 0);
                    else
                        res = interpolable.InterpolateAfter(left.Value.value, left.Value.value, 0);

                    left = default;
                }
                else if (right.Value != null)
                {
                    if (before)
                        res = interpolable.InterpolateBefore(right.Value.value, right.Value.value, 0);
                    else
                        res = interpolable.InterpolateAfter(right.Value.value, right.Value.value, 0);

                    right = default;
                }
                if (res == false)
                    _toDelete.Add(interpolable);
            });
        }

        private float ParseTime(string timeString)
        {
            string[] timeComponents = timeString.Split(':');
            if (timeComponents.Length != 2)
                return -1;
            int minutes;
            if (int.TryParse(timeComponents[0], out minutes) == false || minutes < 0)
                return -1;
            float seconds;
            if (float.TryParse(timeComponents[1], out seconds) == false)
                return -1;
            return minutes * 60 + seconds;
        }

        // Estimate the real time duration when timescale changes from startTimescale to endTimescale over duration according to curve.
        private static float IntegrateTimescaleReciprocal(AnimationCurve curve, float startTimescale, float endTimescale, float duration)
        {
            const int STEPS_PER_SECOND = 20;
            int steps = Mathf.FloorToInt(STEPS_PER_SECOND * duration);
            steps = Math.Max(steps, STEPS_PER_SECOND);

            Func<float, float> reciprocal = (t) =>
            {
                float value = startTimescale + curve.Evaluate(t) * (endTimescale - startTimescale);
                return Mathf.Approximately(value, 0f) ? 0f : 1f / value;
            };

            float total = 0f;
            float dt = 1f / steps;

            for (int i = 0; i < steps; i++)
            {
                float t = i * dt;

                float k1 = dt * reciprocal(t);
                float k2 = dt * reciprocal(t + dt / 2);
                float k3 = dt * reciprocal(t + dt / 2);
                float k4 = dt * reciprocal(t + dt);

                total += (k1 + 2 * k2 + 2 * k3 + k4) / 6;
            }

            return total * duration;
        }            
           
        #region Main Window
        private void UpdateCursor()
        {
            _cursorPoint = _cursor.anchoredPosition; // 현재 y 값을 유지
            _cursorPoint.x = (_playbackTime * _grid.rect.width) / _duration;
            _cursor.anchoredPosition = _cursorPoint;

            UpdateCursor2();

            _timeInputField.text = $"{Mathf.FloorToInt(_playbackTime / 60):00}:{(_playbackTime % 60):00.000}";
        }

        private void UpdateDesiredFrameRate(string s)
        {
            int res;
            if (int.TryParse(_frameRateInputField.text, out res) && res >= 1)
                _desiredFrameRate = res;
            _frameRateInputField.text = _desiredFrameRate.ToString();
        }

        private void UpdatePlaybackTime(string s)
        {
            if (_isPlaying == false)
            {
                float time = ParseTime(_timeInputField.text);
                if (time < 0)
                    return;
                SeekPlaybackTime(time % _duration);
            }
        }

        private void UpdateDuration(string s)
        {
            float time = ParseTime(_durationInputField.text);
            if (time < 0)
                return;
            _duration = time;
            UpdateGrid();
        }

        private void UpdateBlockLength(string arg0)
        {
            float res;
            if (float.TryParse(_blockLengthInputField.text, out res) && res >= 0.01f)
            {
                _blockLength = res;
                UpdateGrid();
            }
            _blockLengthInputField.text = _blockLength.ToString();
        }

        private void UpdateDivisions(string arg0)
        {
            int res;
            if (int.TryParse(_divisionsInputField.text, out res) && res >= 1)
            {
                _divisions = res;
                UpdateGridMaterial();
            }
            _divisionsInputField.text = _divisions.ToString();
        }

        private void UpdateSpeed(string arg0)
        {
            float s;
            if (float.TryParse(_speedInputField.text, out s) && s >= 0)
                Time.timeScale = s;
        }

        private void ZoomOut()
        {
            _zoomLevel -= 0.05f * _zoomLevel;
            if (_zoomLevel < 0.1f)
                _zoomLevel = 0.1f;
            float position = _horizontalScrollView.horizontalNormalizedPosition;
            UpdateGrid();
            _horizontalScrollView.horizontalNormalizedPosition = position;
        }

        private void ZoomIn()
        {
            _zoomLevel += 0.05f * _zoomLevel;
            if (_zoomLevel > 64f)
                _zoomLevel = 64f;
            float position = _horizontalScrollView.horizontalNormalizedPosition;
            UpdateGrid();
            _horizontalScrollView.horizontalNormalizedPosition = position;
        }

        private void ToggleHelp()
        {
            _helpPanel.gameObject.SetActive(!_helpPanel.gameObject.activeSelf);
        }

        private void InterpolablesSearch(string arg0)
        {
            UpdateFilterRegex(arg0);
            UpdateInterpolablesView();
            _verticalScrollView.verticalNormalizedPosition = 1f;    // Reset scroll position
        }

        private void UpdateFilterRegex(string filterText)
        {
            filterText = filterText.Trim();

            if (string.IsNullOrEmpty(filterText))
            {
                _interpolablesSearchRegex = new Regex(".*", RegexOptions.IgnoreCase);
                return;
            }

            var filters = filterText.Split('|');
            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < filters.Length; ++i)
            {
                var filter = filters[i].Trim();

                if (string.IsNullOrEmpty(filter))
                    continue;

                if (builder.Length > 0)
                    builder.Append('|');

                var fs = filter.Split('&', ',')
                    .Select(s => Regex.Escape(s.Trim()).Replace("\\?", ".").Replace("\\*", ".*"))
                    .Where(s => s.Length > 0)
                    .ToArray();

                if (fs.Length <= 0)
                    continue;

                int[] indices = new int[fs.Length];
                for (int j = 0; j < indices.Length; ++j) indices[j] = j;

                //Reorder the filter keywords so that they can be entered in any order.
                while (true)
                {
                    builder.Append("(");

                    for (int j = 0; j < fs.Length; ++j)
                    {
                        builder.Append(".*");
                        builder.Append(fs[indices[j]]);
                    }

                    builder.Append(".*)");

                    if (NextPermutation(indices))
                        builder.Append('|');
                    else
                        break;
                }
            }

            try
            {
                if (builder.Length > 0)
                {
                    _interpolablesSearchRegex = new Regex(builder.ToString(), RegexOptions.IgnoreCase);
                    return;
                }
            }
            catch (System.Exception e)
            {
                Logger.LogError(e);
            }

            _interpolablesSearchRegex = new Regex(".*", RegexOptions.IgnoreCase);
        }

        private static bool NextPermutation(int[] array)
        {
            int i = array.Length - 2;
            while (i >= 0 && array[i] >= array[i + 1])
            {
                i--;
            }

            if (i < 0)
            {
                return false;
            }

            int j = array.Length - 1;
            while (array[j] <= array[i])
            {
                j--;
            }

            int tmp = array[i];
            array[i] = array[j];
            array[j] = tmp;

            Array.Reverse(array, i + 1, array.Length - (i + 1));
            return true;
        }

        private bool IsFilterInterpolationMatch(InterpolableModel interpolableModel)
        {
            if (interpolableModel is Interpolable interporable && _interpolablesSearchRegex.IsMatch(interporable.alias))
                return true;

            return _interpolablesSearchRegex.IsMatch(interpolableModel.name);
        }

        private void UpdateInterpolablesView()
        {
            bool showAll = _allToggle.isOn;
            int interpolableDisplayIndex = 0;
            int headerDisplayIndex = 0;
            //Dictionary<int, Interpolable> usedInterpolables = new Dictionary<int, Interpolable>();
            _gridHeights.Clear();
            float height = 0;
            UpdateInterpolablesViewTree(_interpolablesTree.tree, showAll, ref interpolableDisplayIndex, ref headerDisplayIndex, ref height);
            int interpolableModelDisplayIndex = 0;
            foreach (KeyValuePair<string, List<InterpolableModel>> ownerPair in _interpolableModelsDictionary.OrderBy(p => _hardCodedOwnerOrder.TryGetValue(p.Key, out int order) ? order : int.MaxValue))
            {
                HeaderDisplay header = GetHeaderDisplay(headerDisplayIndex);
                header.gameObject.transform.SetAsLastSibling();
                header.container.offsetMin = Vector2.zero;
                header.group = null;
                header.name.text = ownerPair.Key;
                height += interpolableHeight;
                _gridHeights.Add(height);

                if (header.expanded)
                {
                    foreach (InterpolableModel model in ownerPair.Value)
                    {
                        //Interpolable usedInterpolable;
                        if ( /*usedInterpolables.TryGetValue(model.GetHashCode(), out usedInterpolable) ||*/ model.IsCompatibleWithTarget(_selectedOCI) == false)
                            continue;

                        if (!IsFilterInterpolationMatch(model))
                            continue;

                        InterpolableModelDisplay display = GetInterpolableModelDisplay(interpolableModelDisplayIndex);
                        display.gameObject.transform.SetAsLastSibling();
                        display.model = model;
                        display.name.text = model.name;
                        display.layoutElement.preferredHeight = interpolableHeight;
                        height += interpolableHeight;
                        _gridHeights.Add(height);
                        ++interpolableModelDisplayIndex;
                    }
                }

                ++headerDisplayIndex;
            }

            for (; headerDisplayIndex < _displayedOwnerHeader.Count; headerDisplayIndex++)
                _displayedOwnerHeader[headerDisplayIndex].gameObject.SetActive(false);

            for (; interpolableDisplayIndex < _displayedInterpolables.Count; ++interpolableDisplayIndex)
            {
                InterpolableDisplay display = _displayedInterpolables[interpolableDisplayIndex];
                display.gameObject.SetActive(false);
                display.gridBackground.gameObject.SetActive(false);
            }

            for (; interpolableModelDisplayIndex < _displayedInterpolableModels.Count; ++interpolableModelDisplayIndex)
                _displayedInterpolableModels[interpolableModelDisplayIndex].gameObject.SetActive(false);

            UpdateInterpolableSelection();

            this.ExecuteDelayed2(UpdateGrid);

            this.ExecuteDelayed2(UpdateSeparators, 2);

            TimelineButton.UpdateButton();
        }

        private void UpdateInterpolablesViewTree(List<INode> nodes, bool showAll, ref int interpolableDisplayIndex, ref int headerDisplayIndex, ref float height, int indent = 0)
        {
            foreach (INode node in nodes)
            {
                switch (node.type)
                {
                    case INodeType.Leaf:
                        Interpolable interpolable = ((LeafNode<Interpolable>)node).obj;
                        if (ShouldShowInterpolable(interpolable, showAll) == false)
                            continue;                        

                        InterpolableDisplay display = GetInterpolableDisplay(interpolableDisplayIndex);
                        display.gameObject.transform.SetAsLastSibling();
                        display.container.offsetMin = new Vector2(indent, 0f);
                        display.interpolable = (LeafNode<Interpolable>)node;
                        display.group.alpha = interpolable.useOciInHash == false || interpolable.oci != null && interpolable.oci == _selectedOCI ? 1f : 0.75f;
                        display.enabled.onValueChanged = new Toggle.ToggleEvent();
                        display.enabled.isOn = interpolable.enabled;
                        display.enabled.onValueChanged.AddListener(b => interpolable.enabled = display.enabled.isOn);
                        if (string.IsNullOrEmpty(interpolable.alias))
                        {
                            if (showAll && interpolable.oci != null && ReferenceEquals(interpolable.parameter, interpolable.oci.guideObject) == false)
                                display.name.text = interpolable.name + " (" + interpolable.oci.guideObject.transformTarget.name + ")";
                            else
                                display.name.text = interpolable.name;
                        }
                        else
                            display.name.text = interpolable.alias;
                        display.gridBackground.gameObject.SetActive(true);
                        display.gridBackground.rectTransform.SetRect(new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -height - interpolableHeight), new Vector2(0f, -height));
                        UpdateInterpolableColor(display, interpolable.color);
                        display.layoutElement.preferredHeight = interpolableHeight;
                        height += interpolableHeight;
                        _gridHeights.Add(height);
                    
                        ++interpolableDisplayIndex;
                        break;
                    case INodeType.Group:
                        GroupNode<InterpolableGroup> group = (GroupNode<InterpolableGroup>)node;

                        if (_interpolablesTree.Any(group, leafNode => ShouldShowInterpolable(leafNode.obj, showAll)) == false)
                            break;

                        HeaderDisplay headerDisplay = GetHeaderDisplay(headerDisplayIndex, true);
                        headerDisplay.gameObject.transform.SetAsLastSibling();
                        headerDisplay.container.offsetMin = new Vector2(indent, 0f);
                        headerDisplay.group = (GroupNode<InterpolableGroup>)node;
                        headerDisplay.name.text = group.obj.name;
                        height += Math.Max(interpolableHeight * 2f / 3f, _interpolableMinHeight);
                        _gridHeights.Add(height);
                        ++headerDisplayIndex;
                        if (group.obj.expanded)
                            UpdateInterpolablesViewTree(((GroupNode<InterpolableGroup>)node).children, showAll, ref interpolableDisplayIndex, ref headerDisplayIndex, ref height, indent + 8);
                        break;
                }
            }
        }

        private bool ShouldShowInterpolable(Interpolable interpolable, bool showAll)
        {
            if (showAll == false && ((interpolable.oci != null && interpolable.oci != _selectedOCI) || !interpolable.ShouldShow()))
                return false;
            //if (usedInterpolables.ContainsKey(interpolable.GetBaseHashCode()) == false)
            //    usedInterpolables.Add(interpolable.GetBaseHashCode(), interpolable);

            if (!IsFilterInterpolationMatch(interpolable))
                return false;
            return true;
        }

        private void UpdateInterpolableColor(InterpolableDisplay display, Color c)
        {
            display.background.color = c;
            display.name.color = c.GetContrastingColor();
            display.gridBackground.color = new Color(c.r, c.g, c.b, 0.825f);
        }

        private void UpdateSeparators()
        {
            int i = 0;
            foreach (float height in _gridHeights)
            {
                RawImage separator;
                if (i < _interpolableSeparators.Count)
                    separator = _interpolableSeparators[i];
                else
                {
                    separator = UIUtility.CreateRawImage("Separator", _miscContainer);
                    separator.color = new Color(0f, 0f, 0f, 0.5f);
                    _interpolableSeparators.Add(separator);
                }
                separator.gameObject.SetActive(true);
                separator.rectTransform.SetRect(new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -height - 1.5f), new Vector2(0f, -height + 1.5f));

                ++i;
            }

            for (; i < _interpolableSeparators.Count; i++)
                _interpolableSeparators[i].gameObject.SetActive(false);
        }

        private InterpolableDisplay GetInterpolableDisplay(int i)
        {
            InterpolableDisplay display;
            if (i < _displayedInterpolables.Count)
                display = _displayedInterpolables[i];
            else
            {
                display = CreateInterpolableDisplay(i);
                _displayedInterpolables.Add(display);
            }
            display.gameObject.SetActive(true);
            return display;
        }

        private InterpolableModelDisplay GetInterpolableModelDisplay(int i)
        {
            InterpolableModelDisplay display;
            if (i < _displayedInterpolableModels.Count)
                display = _displayedInterpolableModels[i];
            else
            {
                display = CreateInterpolableModelDisplay();
                _displayedInterpolableModels.Add(display);
            }

            display.gameObject.SetActive(true);
            return display;
        }

        private HeaderDisplay GetHeaderDisplay(int i, bool treeHeader = false)
        {
            HeaderDisplay display;
            if (i < _displayedOwnerHeader.Count)
                display = _displayedOwnerHeader[i];
            else
            {
                display = new HeaderDisplay();
                display.gameObject = GameObject.Instantiate(_headerPrefab);
                display.gameObject.hideFlags = HideFlags.None;
                display.layoutElement = display.gameObject.GetComponent<LayoutElement>();
                display.container = (RectTransform)display.gameObject.transform.Find("Container");
                display.name = display.container.Find("Text").GetComponent<Text>();
                display.inputField = display.container.Find("InputField").GetComponent<InputField>();

                display.gameObject.transform.SetParent(_verticalScrollView.content);
                display.gameObject.transform.localPosition = Vector3.zero;
                display.gameObject.transform.localScale = Vector3.one;
                display.inputField.gameObject.SetActive(false);

                display.container.gameObject.AddComponent<PointerDownHandler>().onPointerDown = (e) =>
                {
                    switch (e.button)
                    {
                        case PointerEventData.InputButton.Left:
                            if (display.group != null)
                                display.group.obj.expanded = !display.group.obj.expanded;
                            else
                                display.expanded = !display.expanded;
                            UpdateInterpolablesView();
                            break;
                        case PointerEventData.InputButton.Middle:
                            if (display.group != null && Input.GetKey(KeyCode.LeftControl))
                            {
#if FEATURE_UNDO
                                UndoPushAction();
#endif
                                List<Interpolable> interpolables = new List<Interpolable>();
                                _interpolablesTree.Recurse(display.group, (n, d) =>
                                {
                                    if (n.type == INodeType.Leaf)
                                        interpolables.Add(((LeafNode<Interpolable>)n).obj);
                                });

                                RemoveInterpolables(interpolables);
                                _interpolablesTree.Remove(display.group);
                            }
                            break;
                        case PointerEventData.InputButton.Right:
                            if (display.group != null && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_ui.transform, e.position, e.pressEventCamera, out Vector2 localPoint))
                            {
                                if (_selectedInterpolables.Count != 0)
                                    ClearSelectedInterpolables();

                                List<AContextMenuElement> elements = new List<AContextMenuElement>();

                                elements.Add(new LeafElement()
                                {
                                    icon = _renameSprite,
                                    text = "Rename",
                                    onClick = p =>
                                    {
                                        display.inputField.gameObject.SetActive(true);
                                        display.inputField.onEndEdit = new InputField.SubmitEvent();
                                        display.inputField.text = display.@group.obj.name;
                                        display.inputField.onEndEdit.AddListener(s =>
                                        {
#if FEATURE_UNDO
                                            UndoPushAction();
#endif
                                            string newName = display.inputField.text.Trim();
                                            if (newName.Length != 0)
                                                display.group.obj.name = newName;
                                            display.inputField.gameObject.SetActive(false);
                                            UpdateInterpolablesView();
                                        });
                                        display.inputField.ActivateInputField();
                                        display.inputField.Select();
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _selectAllSprite,
                                    text = "Select Interpolables under",
                                    onClick = p =>
                                    {
                                        List<Interpolable> toSelect = new List<Interpolable>();
                                        _interpolablesTree.Recurse(display.group, (n, d) =>
                                        {
                                            if (n.type == INodeType.Leaf)
                                                toSelect.Add(((LeafNode<Interpolable>)n).obj);
                                        });
                                        SelectInterpolable(toSelect.ToArray());
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _selectAllSprite,
                                    text = "Select keyframes",
                                    onClick = p =>
                                    {
                                        List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
                                        _interpolablesTree.Recurse(display.group, (n, d) =>
                                        {
                                            if (n.type == INodeType.Leaf)
                                                toSelect.AddRange(((LeafNode<Interpolable>)n).obj.keyframes);
                                        });
                                        SelectKeyframes(toSelect);
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _selectAllSprite,
                                    text = "Select keyframes before cursor",
                                    onClick = p =>
                                    {
                                        List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
                                        float currentTime = _playbackTime % _duration;
                                        _interpolablesTree.Recurse(display.group, (n, d) =>
                                        {
                                            if (n.type == INodeType.Leaf)
                                                toSelect.AddRange(((LeafNode<Interpolable>)n).obj.keyframes.Where(k => k.Key < currentTime));
                                        });
                                        SelectKeyframes(toSelect);
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _selectAllSprite,
                                    text = "Select keyframes after cursor",
                                    onClick = p =>
                                    {
                                        List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
                                        float currentTime = _playbackTime % _duration;
                                        _interpolablesTree.Recurse(display.group, (n, d) =>
                                        {
                                            if (n.type == INodeType.Leaf)
                                                toSelect.AddRange(((LeafNode<Interpolable>)n).obj.keyframes.Where(k => k.Key >= currentTime));
                                        });
                                        SelectKeyframes(toSelect);
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _addSprite,
                                    text = "Add keyframes at cursor",
                                    onClick = p =>
                                    {
#if FEATURE_UNDO
                                        UndoPushAction();
#endif
                                        float time = _playbackTime % _duration;
                                        _interpolablesTree.Recurse(display.group, (n, d) =>
                                        {
                                            if (n.type == INodeType.Leaf)
                                                AddKeyframe(((LeafNode<Interpolable>)n).obj, time);
                                        });
#if FIXED_096_DEBUG
                                        SetObjectCtrlDirty(_selectedOCI);
#endif                                        
                                        UpdateGrid();
                                    }
                                });
                                var treeGroups = GetInterpolablesTreeGroups(new List<INode> { display.group });
                                if (treeGroups.Count > 0)
                                {
                                    elements.Add(new GroupElement()
                                    {
                                        icon = _addToFolderSprite,
                                        text = "Parent to",
                                        elements = treeGroups
                                    });
                                }
                                elements.Add(new LeafElement()
                                {
                                    icon = _checkboxSprite,
                                    text = "Disable",
                                    onClick = p =>
                                    {
                                        _interpolablesTree.Recurse(display.group, (n, d) =>
                                        {
                                            if (n.type == INodeType.Leaf)
                                                ((LeafNode<Interpolable>)n).obj.enabled = false;
                                        });
                                        UpdateInterpolablesView();
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _checkboxCompositeSprite,
                                    text = "Enable",
                                    onClick = p =>
                                    {
                                        _interpolablesTree.Recurse(display.group, (n, d) =>
                                        {
                                            if (n.type == INodeType.Leaf)
                                                ((LeafNode<Interpolable>)n).obj.enabled = true;
                                        });
                                        UpdateInterpolablesView();
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _chevronUpSprite,
                                    text = "Move up",
                                    onClick = p =>
                                    {
#if FEATURE_UNDO
                                        UndoPushAction();
#endif
                                        _interpolablesTree.MoveUp(display.group);
                                        UpdateInterpolablesView();
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _chevronDownSprite,
                                    text = "Move down",
                                    onClick = p =>
                                    {
#if FEATURE_UNDO
                                        UndoPushAction();
#endif
                                        _interpolablesTree.MoveDown(display.group);
                                        UpdateInterpolablesView();
                                    }
                                });
                                elements.Add(new LeafElement()
                                {
                                    icon = _deleteSprite,
                                    text = "Delete",
                                    onClick = p =>
                                    {
                                        UIUtility.DisplayConfirmationDialog(result =>
                                        {
                                            if (result)
                                            {
#if FEATURE_UNDO
                                                UndoPushAction();
#endif
                                                List<Interpolable> interpolables = new List<Interpolable>();
                                                _interpolablesTree.Recurse(display.group, (n, d) =>
                                                {
                                                    if (n.type == INodeType.Leaf)
                                                        interpolables.Add(((LeafNode<Interpolable>)n).obj);
                                                });

                                                _interpolablesTree.Remove(display.group);
                                                RemoveInterpolables(interpolables);
                                            }
                                        }, "Are you sure you want to delete this group?");
                                    }
                                });
                                UIUtility.ShowContextMenu(_ui, localPoint, elements, 180);
                            }
                            break;
                    }
                };

                _displayedOwnerHeader.Add(display);
            }
            display.gameObject.SetActive(true);
            display.layoutElement.preferredHeight = treeHeader ? Math.Max(interpolableHeight * 2f / 3f, _interpolableMinHeight) : interpolableHeight;
            return display;
        }

        private List<AContextMenuElement> GetInterpolablesTreeGroups(ICollection<INode> toParent)
        {
            var groupsToIgnore = new List<IGroupNode>();

            // Ensure that groups can't be parented to a group that they are a parent of
            var groupNodes = toParent.OfType<IGroupNode>().ToList();
            var hasGroups = groupNodes.Count > 0;
            if (hasGroups)
                groupsToIgnore.AddRange(groupNodes.Map(node => node.children.OfType<IGroupNode>()));

            // If all items have the same parent, remove it from the options
            var parents = toParent.Select(n => n.parent).Distinct().ToList();
            if (parents.Count == 1 && parents[0] != null)
            {
                groupsToIgnore.Add(parents[0]);
            }

            var possibleParents = RecurseInterpolablesTreeGroups(_interpolablesTree.tree, toParent, groupsToIgnore, hasGroups);

            if (parents.Count != 1 || parents[0] != null)
            {
                possibleParents.Insert(0, new LeafElement()
                {
                    text = "Nothing",
                    onClick = p =>
                    {
                        _interpolablesTree.ParentTo(toParent, null);
                        UpdateInterpolablesView();
                    }
                });
            }

            return possibleParents;
        }

        private List<AContextMenuElement> RecurseInterpolablesTreeGroups(List<INode> nodes, ICollection<INode> toParent, ICollection<IGroupNode> toIgnore, bool ignoreChildren)
        {
            var elements = new List<AContextMenuElement>();

            foreach (var group in nodes.OfType<GroupNode<InterpolableGroup>>())
            {
                var ignored = toIgnore.Contains(group);
                if (!ignored)
                {
                    elements.Add(new LeafElement()
                    {
                        icon = _addToFolderSprite,
                        text = group.obj.name,
                        onClick = p =>
                        {
                            _interpolablesTree.ParentTo(toParent, group);
                            UpdateInterpolablesView();
                        }
                    });
                }
                if (!ignored || !ignoreChildren)
                {
                    var subElements = RecurseInterpolablesTreeGroups(group.children, toParent, toIgnore, ignoreChildren);
                    if (subElements.Count > 0)
                    {
                        elements.Add(new GroupElement()
                        {
                            text = group.obj.name,
                            elements = subElements
                        });
                    }
                }
            }
            return elements;
        }

        private void HighlightInterpolable(Interpolable interpolable, bool scrollTo = true)
        {
            InterpolableDisplay display = _displayedInterpolables.FirstOrDefault(d => d.interpolable.obj == interpolable);
            if (display == null)
                return;

            if (scrollTo)
            {
                var rectTransform = (RectTransform)display.container.parent;
                var parent = (RectTransform)rectTransform.parent;
                var view = (RectTransform)parent.parent;

                float scrollY = -rectTransform.anchoredPosition.y - view.rect.height * 0.5f;
                scrollY = Mathf.Clamp(scrollY, 0, parent.rect.height - view.rect.height * 0.5f);

                var position = parent.anchoredPosition;
                position.y = scrollY;
                parent.anchoredPosition = position;
            }

            StartCoroutine(HighlightInterpolable_Routine(display, interpolable));
        }

        private IEnumerator HighlightInterpolable_Routine(InterpolableDisplay display, Interpolable interpolable)
        {
            if (display != null)
            {
                Color first = interpolable.color.GetContrastingColor();
                Color second = first.GetContrastingColor();
                float startTime = Time.unscaledTime;
                while (Time.unscaledTime - startTime < 0.25f)
                {
                    UpdateInterpolableColor(display, Color.Lerp(interpolable.color, first, (Time.unscaledTime - startTime) * 4f));
                    yield return null;
                }
                startTime = Time.unscaledTime;
                while (Time.unscaledTime - startTime < 1f)
                {
                    UpdateInterpolableColor(display, Color.Lerp(second, first, (Mathf.Cos((Time.unscaledTime - startTime) * Mathf.PI * 4) + 1f) / 2f));
                    yield return null;
                }
                startTime = Time.unscaledTime;
                while (Time.unscaledTime - startTime < 0.25f)
                {
                    UpdateInterpolableColor(display, Color.Lerp(first, interpolable.color, (Time.unscaledTime - startTime) * 4f));
                    yield return null;
                }
                UpdateInterpolableColor(display, interpolable.color);
            }
        }

        private void PotentiallyBeginAreaSelect(PointerEventData e)
        {
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_keyframesContainer, e.position, e.pressEventCamera, out localPoint))
            {
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    float time = 10f * localPoint.x / (_baseGridWidth * _zoomLevel);
                    float beat = _blockLength / _divisions;
                    float mod = time % beat;
                    if (mod / beat > 0.5f)
                        time += beat - mod;
                    else
                        time -= mod;
                    localPoint.x = time * (_baseGridWidth * _zoomLevel) / 10f;
                }
                _areaSelectFirstPoint = localPoint;
            }
            _isAreaSelecting = false;
        }

        private void BeginAreaSelect(PointerEventData e)
        {
            _isAreaSelecting = true;
            _selectionArea.gameObject.SetActive(true);
        }

        private void UpdateAreaSelect(PointerEventData e)
        {
            if (_isAreaSelecting == false)
                return;
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_keyframesContainer, e.position, e.pressEventCamera, out localPoint))
            {
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    float time = 10f * localPoint.x / (_baseGridWidth * _zoomLevel);
                    float beat = _blockLength / _divisions;
                    float mod = time % beat;
                    if (mod / beat > 0.5f)
                        time += beat - mod;
                    else
                        time -= mod;
                    localPoint.x = time * (_baseGridWidth * _zoomLevel) / 10f;
                }
                Vector2 min = new Vector2(Mathf.Min(_areaSelectFirstPoint.x, localPoint.x), Mathf.Min(_areaSelectFirstPoint.y, localPoint.y));
                Vector2 max = new Vector2(Mathf.Max(_areaSelectFirstPoint.x, localPoint.x), Mathf.Max(_areaSelectFirstPoint.y, localPoint.y));

                if (Input.GetKey(KeyCode.LeftAlt))
                {
                    //Maximize the top and bottom of the selection
                    var rect = _keyframesContainer.rect;
                    min.y = rect.yMin;
                    max.y = rect.yMax;
                }

                _selectionArea.offsetMin = min;
                _selectionArea.offsetMax = max;
            }
        }

        private void EndAreaSelect(PointerEventData e)
        {
            Vector2 localPoint;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_keyframesContainer, e.position, e.pressEventCamera, out localPoint))
                return;
            float firstTime = 10f * _areaSelectFirstPoint.x / (_baseGridWidth * _zoomLevel);
            float secondTime = 10f * localPoint.x / (_baseGridWidth * _zoomLevel);
            if (Input.GetKey(KeyCode.LeftShift))
            {
                float beat = _blockLength / _divisions;
                float mod = secondTime % beat;
                if (mod / beat > 0.5f)
                    secondTime += beat - mod;
                else
                    secondTime -= mod;
            }
            if (secondTime < firstTime)
            {
                float temp = firstTime;
                firstTime = secondTime;
                secondTime = temp;
            }
            float minY = Mathf.Min(_areaSelectFirstPoint.y, localPoint.y);
            float maxY = Mathf.Max(_areaSelectFirstPoint.y, localPoint.y);

            if (Input.GetKey(KeyCode.LeftAlt))
            {
                //Maximize the top and bottom of the selection
                var rect = _keyframesContainer.rect;
                minY = rect.yMin;
                maxY = rect.yMax;
            }

            _selectionArea.gameObject.SetActive(false);
            _isAreaSelecting = false;

            List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
            foreach (InterpolableDisplay display in _displayedInterpolables)
            {
                if (display.gameObject.activeSelf == false)
                    break;
                float height = ((RectTransform)display.gameObject.transform).anchoredPosition.y;

                if (height > minY && height < maxY)
                {
                    foreach (KeyValuePair<float, Keyframe> pair in display.interpolable.obj.keyframes)
                    {
                        if (pair.Key >= firstTime && pair.Key <= secondTime)
                            toSelect.Add(pair);
                    }

                }
            }
            if (Input.GetKey(KeyCode.LeftControl))
                SelectAddKeyframes(toSelect);
            else
                SelectKeyframes(toSelect);
        }

        private void SelectAddInterpolable(params Interpolable[] interpolables)
        {
            foreach (Interpolable interpolable in interpolables)
            {
                int index = _selectedInterpolables.FindIndex(k => k == interpolable);
                if (index != -1)
                    _selectedInterpolables.RemoveAt(index);
                else
                    _selectedInterpolables.Add(interpolable);
            }
            UpdateInterpolableSelection();
        }

        private void SelectInterpolable(params Interpolable[] interpolables)
        {
            _selectedInterpolables.Clear();
            SelectAddInterpolable(interpolables);
        }

        private void ClearSelectedInterpolables()
        {
            _selectedInterpolables.Clear();
            UpdateInterpolableSelection();
        }

        private void UpdateInterpolableSelection()
        {
            foreach (InterpolableDisplay display in _displayedInterpolables)
            {
                bool selected = _selectedInterpolables.Any(e => e == display.interpolable.obj);
                display.selectedOutline.gameObject.SetActive(selected);
                display.background.material.SetFloat("_DrawChecker", selected ? 1f : 0f);
                display.gridBackground.material.SetFloat("_DrawChecker", selected ? 1f : 0f);
                display.name.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
                // Forcing the texture to refresh
                display.background.enabled = false;
                display.background.enabled = true;
                display.gridBackground.enabled = false;
                display.gridBackground.enabled = true;
            }
        }

#if FIXED_096
        private void UpdateGrid()
        {            
            if (!_ui.gameObject.activeSelf)
                return;

#if FIXED_096_DEBUG
            SetObjectCtrlDirty(_selectedOCI);
#endif                
            _durationInputField.text = $"{Mathf.FloorToInt(_duration / 60):00}:{(_duration % 60):00.00}";

            _horizontalScrollView.content.sizeDelta = new Vector2(_baseGridWidth * _zoomLevel * _duration / 10f, _horizontalScrollView.content.sizeDelta.y);
            UpdateGridMaterial();
            int max = Mathf.CeilToInt(_duration / _blockLength);
            int textIndex = 0;
            for (int i = 1; i < max; i++)
            {
                Text t;
                if (textIndex < _timeTexts.Count)
                    t = _timeTexts[textIndex];
                else
                {
                    t = UIUtility.CreateText("Time " + textIndex, _textsContainer);
                    t.alignByGeometry = true;
                    t.alignment = TextAnchor.MiddleCenter;
                    t.color = Color.white;
                    t.raycastTarget = false;
                    t.rectTransform.SetRect(Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(60f, 0f));
                    _timeTexts.Add(t);
                }
                t.text = $"{Mathf.FloorToInt((i * _blockLength) / 60):00}:{((i * _blockLength) % 60):00.##}";
                t.gameObject.SetActive(true);
                t.rectTransform.anchoredPosition = new Vector2(i * _blockLength * _baseGridWidth * _zoomLevel / 10, t.rectTransform.anchoredPosition.y);
                ++textIndex;
            }
            for (; textIndex < _timeTexts.Count; textIndex++)
                _timeTexts[textIndex].gameObject.SetActive(false);

            if (_allToggle.isOn)
            {
                foreach (KeyValuePair<int, ObjectCtrlItem> pair in _ociControlMgmt)
                {                    
                    UpdateKeyframeInfo(GetOciControlInfo(pair.Value.oci));
                }
            }
            else
            {                
                if (_selectedOCI != null)
                    UpdateKeyframeInfo(GetOciControlInfo(null));
                UpdateKeyframeInfo(GetOciControlInfo(_selectedOCI));             
            }

            UpdateKeyframeSelection();

            UpdateCursor();

            this.ExecuteDelayed2(() => _keyframesContainer.sizeDelta = new Vector2(_keyframesContainer.sizeDelta.x, _verticalScrollView.content.rect.height), 2);
        }

        private ObjectCtrlItem GetOciControlInfo(ObjectCtrlInfo oci) {
            
            int hashcode = 0;

            if (oci != null)
                hashcode = oci.GetHashCode();         
                
            ObjectCtrlItem objectCtrlItem = null;
            if (_ociControlMgmt.TryGetValue(hashcode, out objectCtrlItem))
            {
                return objectCtrlItem;
            }

            return null;
        }

        private void UpdateKeyframeInfo(ObjectCtrlItem objectCtrlItem) {

            if (objectCtrlItem == null)
                return;

            GameObject keyframeGroup = objectCtrlItem.keyframeGroup;
            if (keyframeGroup != null) {
                keyframeGroup.SetActive(true);
                keyframeGroup.transform.localPosition = new Vector3(keyframeGroup.transform.localPosition.x, 0, keyframeGroup.transform.localPosition.z);

                if (objectCtrlItem.dirty)
                {
                    if (_interpolablesTree.tree.Count > 0) {
                        int interpolableIndex = 0;
                        int keyframeIndex = 0;

                        UpdateKeyframesTree(_interpolablesTree.tree, ref objectCtrlItem, ref interpolableIndex, ref keyframeIndex);    

                        int deleteCnt = objectCtrlItem.displayedKeyframes.Count - keyframeIndex;

                        if (deleteCnt > 0)
                        {
                            int startIndex = objectCtrlItem.displayedKeyframes.Count - deleteCnt;
                            List<KeyframeDisplay> lastFour = new List<KeyframeDisplay>();
                            lastFour = objectCtrlItem.displayedKeyframes.GetRange(startIndex, deleteCnt);
                            foreach (KeyframeDisplay item in lastFour)
                            {
                                ReturnToKeyframeDisplayPool(item);
                            }
                            objectCtrlItem.displayedKeyframes.RemoveRange(startIndex, deleteCnt);
                        }   
                    }

                    objectCtrlItem.dirty = false;
                }  
            }  
        }
#else
        private void UpdateGrid()
        {
            _durationInputField.text = $"{Mathf.FloorToInt(_duration / 60):00}:{(_duration % 60):00.00}";

            _horizontalScrollView.content.sizeDelta = new Vector2(_baseGridWidth * _zoomLevel * _duration / 10f, _horizontalScrollView.content.sizeDelta.y);
            UpdateGridMaterial();
            int max = Mathf.CeilToInt(_duration / _blockLength);
            int textIndex = 0;
            for (int i = 1; i < max; i++)
            {
                Text t;
                if (textIndex < _timeTexts.Count)
                    t = _timeTexts[textIndex];
                else
                {
                    t = UIUtility.CreateText("Time " + textIndex, _textsContainer);
                    t.alignByGeometry = true;
                    t.alignment = TextAnchor.MiddleCenter;
                    t.color = Color.white;
                    t.raycastTarget = false;
                    t.rectTransform.SetRect(Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(60f, 0f));
                    _timeTexts.Add(t);
                }
                t.text = $"{Mathf.FloorToInt((i * _blockLength) / 60):00}:{((i * _blockLength) % 60):00.##}";
                t.gameObject.SetActive(true);
                t.rectTransform.anchoredPosition = new Vector2(i * _blockLength * _baseGridWidth * _zoomLevel / 10, t.rectTransform.anchoredPosition.y);
                ++textIndex;
            }
            for (; textIndex < _timeTexts.Count; textIndex++)
                _timeTexts[textIndex].gameObject.SetActive(false);


            bool showAll = _allToggle.isOn;
            int keyframeIndex = 0;
            int interpolableIndex = 0;
    
            UpdateKeyframesTree(_interpolablesTree.tree, showAll, ref interpolableIndex, ref keyframeIndex);

            for (; keyframeIndex < _displayedKeyframes.Count; ++keyframeIndex)
            {
                KeyframeDisplay display = _displayedKeyframes[keyframeIndex];
                display.gameObject.SetActive(false);
                display.keyframe = null;
            }

            UpdateKeyframeSelection();

            UpdateCursor();

            this.ExecuteDelayed2(() => _keyframesContainer.sizeDelta = new Vector2(_keyframesContainer.sizeDelta.x, _verticalScrollView.content.rect.height), 2);
        }
#endif
        private void UpdateKeyframesTree(List<INode> nodes, ref ObjectCtrlItem objectCtrlItem, ref int interpolableIndex, ref int keyframeIndex)
        {
            float visible_width = _horizontalScrollView.content.rect.width;            

            foreach (INode node in nodes)
            {
                switch (node.type)
                {
                    case INodeType.Leaf:
                        Interpolable interpolable = ((LeafNode<Interpolable>)node).obj;

                         if (interpolable.oci != objectCtrlItem.oci)
                            continue;

                        if (!IsFilterInterpolationMatch(interpolable))
                            continue;

                        InterpolableDisplay interpolableDisplay = _displayedInterpolables[interpolableIndex];
                        float zoomGridWidth = _baseGridWidth * _zoomLevel;
                        Vector2 tempPos = Vector2.zero;
                        foreach (KeyValuePair<float, Keyframe> keyframePair in interpolable.keyframes)
                        {
                            float x = zoomGridWidth * keyframePair.Key / 10f;

                            if (x > visible_width)
                            {
                                continue;
                            }

                            KeyframeDisplay display;
                            if (keyframeIndex < objectCtrlItem.displayedKeyframes.Count)
                            {                             
                                display = objectCtrlItem.displayedKeyframes[keyframeIndex];
                            }
                            else
                            {
                                display = GetFromKeyframeDisplayPool();
                                display.gameObject.transform.SetParent(objectCtrlItem.keyframeGroup.transform);
                                display.gameObject.transform.localPosition = Vector3.zero;
                                display.gameObject.transform.localScale = Vector3.one;
                                display.gameObject.SetActive(true);
                                objectCtrlItem.displayedKeyframes.Add(display);
                            }
                            tempPos.x = x;
                            tempPos.y = ((RectTransform)interpolableDisplay.gameObject.transform).anchoredPosition.y;

                            ((RectTransform)display.gameObject.transform).anchoredPosition = tempPos;
                            display.keyframe = keyframePair.Value;
                            ++keyframeIndex;
                        }
                        ++interpolableIndex;
                        break;
                    case INodeType.Group:
                        GroupNode<InterpolableGroup> group = (GroupNode<InterpolableGroup>)node;
                        if (group.obj.expanded)
                            UpdateKeyframesTree(group.children, ref objectCtrlItem, ref interpolableIndex, ref keyframeIndex);
                        break;
                }
            }
        }

#if FIXED_096
        private KeyframeDisplay CreateKeyFrameDisplay() {
            KeyframeDisplay display = new KeyframeDisplay();
            display.gameObject = GameObject.Instantiate(_keyframePrefab);
            display.gameObject.hideFlags = HideFlags.None;
            display.image = display.gameObject.GetComponentsInChildren<RawImage>()[1]; // 성능 향상

            display.gameObject.transform.SetParent(null);
            // display.gameObject.transform.SetParent(_keyframesContainer);
            // display.gameObject.transform.localPosition = Vector3.zero;
            // display.gameObject.transform.localScale = Vector3.one;

            PointerEnterHandler pointerEnter = display.gameObject.AddComponent<PointerEnterHandler>();
            pointerEnter.onPointerEnter = (e) =>
            {
                _tooltip.transform.parent.gameObject.SetActive(true);
                float t = display.keyframe.parent.keyframes.First(k => k.Value == display.keyframe).Key;
                _tooltip.text = $"T: {Mathf.FloorToInt(t / 60):00}:{t % 60:00.########}\nV: {display.keyframe.value}";

            };

            pointerEnter.onPointerExit = (e) => { _tooltip.transform.parent.gameObject.SetActive(false); };
            PointerDownHandler pointerDown = display.gameObject.AddComponent<PointerDownHandler>();
            pointerDown.onPointerDown = (e) =>
            {
                if (Input.GetKey(KeyCode.LeftAlt))
                    return;
                switch (e.button)
                {
                    case PointerEventData.InputButton.Left:
                        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                            SelectAddKeyframes(display.keyframe.parent.keyframes.First(k => k.Value == display.keyframe));
                        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        {
                            KeyValuePair<float, Keyframe> lastSelected = _selectedKeyframes.LastOrDefault(k => k.Value.parent == display.keyframe.parent);
                            if (lastSelected.Value != null)
                            {
                                KeyValuePair<float, Keyframe> selectingNow = display.keyframe.parent.keyframes.First(k => k.Value == display.keyframe);
                                float minTime;
                                float maxTime;
                                if (lastSelected.Key < selectingNow.Key)
                                {
                                    minTime = lastSelected.Key;
                                    maxTime = selectingNow.Key;
                                }
                                else
                                {
                                    minTime = selectingNow.Key;
                                    maxTime = lastSelected.Key;
                                }
                                SelectAddKeyframes(display.keyframe.parent.keyframes.Where(k => k.Key > minTime && k.Key < maxTime));
                                SelectAddKeyframes(selectingNow);
                            }
                            else
                                SelectAddKeyframes(display.keyframe.parent.keyframes.First(k => k.Value == display.keyframe));
                        }
                        else
                            SelectKeyframes(display.keyframe.parent.keyframes.First(k => k.Value == display.keyframe));

                        break;
                    case PointerEventData.InputButton.Right:
                        SeekPlaybackTime(display.keyframe.parent.keyframes.First(k => k.Value == display.keyframe).Key);
                        break;
                    case PointerEventData.InputButton.Middle:
                        if (Input.GetKey(KeyCode.LeftControl))
                        {
                            List<KeyValuePair<float, Keyframe>> toDelete = new List<KeyValuePair<float, Keyframe>>();
                            if (Input.GetKey(KeyCode.LeftShift))
                                toDelete.AddRange(_selectedKeyframes);
                            KeyValuePair<float, Keyframe> kPair = display.keyframe.parent.keyframes.FirstOrDefault(k => k.Value == display.keyframe);
                            if (kPair.Value != null)
                                toDelete.Add(kPair);
                            if (toDelete.Count != 0)
                            {
#if FEATURE_UNDO
                                UndoPushAction();
#endif
                                DeleteKeyframes(toDelete);
                                _tooltip.transform.parent.gameObject.SetActive(false);
                            }
                        }
                        break;
                }
            };

            DragHandler dragHandler = display.gameObject.AddComponent<DragHandler>();
            dragHandler.onBeginDrag = e =>
            {
                if (Input.GetKey(KeyCode.LeftAlt) == false)
                    return;
                Vector2 localPoint;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_keyframesContainer, e.position, e.pressEventCamera, out localPoint))
                {   
                    int hashcode = 0;
                    if(_selectedOCI != null)
                        hashcode = _selectedOCI.GetHashCode();

                    if (_self._ociControlMgmt.ContainsKey(hashcode)) {
                        List<KeyframeDisplay> displayedKeyframes = new List<KeyframeDisplay>();
                        foreach (Transform child in _keyframesContainer.transform)
                        {
                            KeyframeDisplay kf = child.GetComponent<KeyframeDisplay>();
                            displayedKeyframes.Add(kf);
                        } 

                        _selectedKeyframesXOffset.Clear();
                        foreach (KeyValuePair<float, Keyframe> selectedKeyframe in _selectedKeyframes)
                        {
                            KeyframeDisplay selectedDisplay = displayedKeyframes.Find(d => d.keyframe == selectedKeyframe.Value);
                            _selectedKeyframesXOffset.Add(selectedDisplay, ((RectTransform)selectedDisplay.gameObject.transform).anchoredPosition.x - localPoint.x);
                        }
                    }
                }

                if (_selectedKeyframesXOffset.Count != 0)
                    isPlaying = false;
                e.Reset();
            };

            dragHandler.onDrag = e =>
            {
                if (_selectedKeyframesXOffset.Count == 0)
                    return;
                Vector2 localPoint;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_keyframesContainer, e.position, e.pressEventCamera, out localPoint))
                {
                    float x = localPoint.x;
                    foreach (KeyValuePair<KeyframeDisplay, float> pair in _selectedKeyframesXOffset)
                    {
                        float localX = localPoint.x + pair.Value;
                        if (localX < 0f)
                            x = localPoint.x - localX;
                    }

                    if (Input.GetKey(KeyCode.LeftShift))
                    {
                        float time = 10f * x / (_baseGridWidth * _zoomLevel);
                        float beat = _blockLength / _divisions;
                        float mod = time % beat;
                        if (mod / beat > 0.5f)
                            time += beat - mod;
                        else
                            time -= mod;
                        x = (time * _baseGridWidth * _zoomLevel) / 10f - _selectedKeyframesXOffset[display];
                    }

                    foreach (KeyValuePair<KeyframeDisplay, float> pair in _selectedKeyframesXOffset)
                    {
                        RectTransform rt = ((RectTransform)pair.Key.gameObject.transform);
                        rt.anchoredPosition = new Vector2(x + pair.Value, rt.anchoredPosition.y);
                    }
                }
                e.Reset();
            };

            dragHandler.onEndDrag = e =>
            {
                if (_selectedKeyframesXOffset.Count == 0)
                    return;
#if FEATURE_UNDO
                UndoPushAction();
#endif
                foreach (KeyValuePair<KeyframeDisplay, float> pair in _selectedKeyframesXOffset)
                {
                    RectTransform rt = ((RectTransform)pair.Key.gameObject.transform);
                    float time = 10f * rt.anchoredPosition.x / (_baseGridWidth * _zoomLevel);
                    MoveKeyframe(pair.Key.keyframe, time);

                    int index = _selectedKeyframes.FindIndex(k => k.Value == pair.Key.keyframe);
                    if (index != -1)
                        _selectedKeyframes[index] = new KeyValuePair<float, Keyframe>(time, pair.Key.keyframe);
                }

                e.Reset();
                UpdateKeyframeWindow(false);
                _selectedKeyframesXOffset.Clear();
            };

            return display;
        }

        private InterpolableModelDisplay CreateInterpolableModelDisplay() {
            InterpolableModelDisplay display = new InterpolableModelDisplay();
            display.gameObject = GameObject.Instantiate(_interpolableModelPrefab);
            display.gameObject.hideFlags = HideFlags.None;
            display.layoutElement = display.gameObject.GetComponent<LayoutElement>();
            display.name = display.gameObject.GetComponentInChildren<Text>();
            display.gameObject.transform.SetParent(_verticalScrollView.content);
            display.gameObject.transform.localPosition = Vector3.zero;
            display.gameObject.transform.localScale = Vector3.one;

            return display;
        }

        private InterpolableDisplay CreateInterpolableDisplay(int i){
            InterpolableDisplay    display = new InterpolableDisplay();
            display.gameObject = GameObject.Instantiate(_interpolablePrefab);
            display.gameObject.hideFlags = HideFlags.None;
            display.layoutElement = display.gameObject.GetComponent<LayoutElement>();
            display.group = display.gameObject.GetComponent<CanvasGroup>();
            display.container = (RectTransform)display.gameObject.transform.Find("Container");
            display.enabled = display.container.GetComponentInChildren<Toggle>();

            display.name = display.container.Find("Label").GetComponent<Text>();
            display.inputField = display.container.Find("InputField").GetComponent<InputField>();
            display.background = display.container.GetComponent<Image>();
            display.selectedOutline = display.container.Find("SelectedOutline").GetComponent<Image>();
            display.gridBackground = UIUtility.CreateRawImage($"Interpolable{i} Background", _miscContainer);
            display.background.material = new Material(display.background.material);

            display.gameObject.transform.SetParent(_verticalScrollView.content);
            display.gameObject.transform.localPosition = Vector3.zero;
            display.gameObject.transform.localScale = Vector3.one;
            display.gridBackground.transform.SetAsFirstSibling();
            display.gridBackground.raycastTarget = false;
            display.gridBackground.material = new Material(_keyframesBackgroundMaterial);
            display.inputField.gameObject.SetActive(false);

            display.container.gameObject.AddComponent<PointerDownHandler>().onPointerDown = (e) =>
            {
                Interpolable interpolable = display.interpolable.obj;
                switch (e.button)
                {
                    case PointerEventData.InputButton.Left:
                        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                            SelectAddInterpolable(interpolable);
                        else if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                        {
                            Interpolable lastSelected = _selectedInterpolables.LastOrDefault();
                            if (lastSelected != null)
                            {
                                Interpolable selectingNow = interpolable;
                                int selectingNowIndex = _displayedInterpolables.FindIndex(elem => elem.interpolable.obj == selectingNow);
                                int lastSelectedIndex = _displayedInterpolables.FindIndex(elem => elem.interpolable.obj == lastSelected);
                                if (selectingNowIndex < lastSelectedIndex)
                                {
                                    int temp = selectingNowIndex;
                                    selectingNowIndex = lastSelectedIndex;
                                    lastSelectedIndex = temp;
                                }

                                SelectAddInterpolable(_displayedInterpolables.Where((elem, index) => index > lastSelectedIndex && index < selectingNowIndex).Select(elem => elem.interpolable.obj).ToArray());
                                SelectAddInterpolable(selectingNow);
                            }
                            else
                                SelectAddInterpolable(interpolable);
                        }
                        else if (Input.GetKey(KeyCode.LeftAlt))
                        {
                            GuideObject linkedGuideObject = interpolable.parameter as GuideObject;
                            if (linkedGuideObject == null && interpolable.oci != null)
                                linkedGuideObject = interpolable.oci.guideObject;
                            if (linkedGuideObject != null)
                                GuideObjectManager.Instance.selectObject = linkedGuideObject;
                        }
                        else
                            SelectInterpolable(interpolable);

                        break;
                    case PointerEventData.InputButton.Middle:
                        if (Input.GetKey(KeyCode.LeftControl))
                        {
#if FEATURE_UNDO
                            UndoPushAction();
#endif
                            RemoveInterpolable(interpolable);
                        }
                        break;
                    case PointerEventData.InputButton.Right:
                        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_ui.transform, e.position, e.pressEventCamera, out Vector2 localPoint))
                        {
                            if (_selectedInterpolables.Count == 0 || _selectedInterpolables.Contains(interpolable) == false)
                                SelectInterpolable(interpolable);

                            List<Interpolable> currentlySelectedInterpolables = new List<Interpolable>(_selectedInterpolables);

                            List<AContextMenuElement> elements = new List<AContextMenuElement>();
                            if (currentlySelectedInterpolables.Count == 1)
                            {
                                Interpolable selectedInterpolable = currentlySelectedInterpolables[0];
                                GuideObject linkedGuideObject = selectedInterpolable.parameter as GuideObject;
                                if (linkedGuideObject == null && selectedInterpolable.oci != null)
                                    linkedGuideObject = selectedInterpolable.oci.guideObject;

                                if (linkedGuideObject != null)
                                {
                                    elements.Add(new LeafElement()
                                    {
                                        icon = _linkSprite,
                                        text = "Select linked GuideObject",
                                        onClick = p => { GuideObjectManager.Instance.selectObject = linkedGuideObject; }
                                    });
                                }

                                elements.Add(new LeafElement()
                                {
                                    icon = _renameSprite,
                                    text = "Rename",
                                    onClick = p =>
                                    {
                                        display.inputField.gameObject.SetActive(true);
                                        display.inputField.onEndEdit = new InputField.SubmitEvent();
                                        display.inputField.text = string.IsNullOrEmpty(selectedInterpolable.alias) ? selectedInterpolable.name : selectedInterpolable.alias;
                                        display.inputField.onEndEdit.AddListener(s =>
                                        {
#if FEATURE_UNDO
                                            UndoPushAction();
#endif
                                            selectedInterpolable.alias = display.inputField.text.Trim();
                                            display.inputField.gameObject.SetActive(false);
                                            UpdateInterpolablesView();
                                        });
                                        display.inputField.ActivateInputField();
                                        display.inputField.Select();
                                    }
                                });
                            }
                            else
                            {
                                elements.Add(new LeafElement()
                                {
                                    icon = _newFolderSprite,
                                    text = "Group together",
                                    onClick = p =>
                                    {
#if FEATURE_UNDO
                                        UndoPushAction();
#endif
                                        _interpolablesTree.GroupTogether(currentlySelectedInterpolables, new InterpolableGroup() { name = "New Group" });
                                        UpdateInterpolablesView();
                                    }
                                });
                            }
                            elements.Add(new LeafElement()
                            {
                                icon = _selectAllSprite,
                                text = "Select keyframes",
                                onClick = p =>
                                {
                                    List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
                                    foreach (Interpolable selected in currentlySelectedInterpolables)
                                        toSelect.AddRange(selected.keyframes);
                                    SelectKeyframes(toSelect);
                                }
                            });
                            elements.Add(new LeafElement()
                            {
                                icon = _selectAllSprite,
                                text = "Select keyframes before cursor",
                                onClick = p =>
                                {
                                    List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
                                    float currentTime = _playbackTime % _duration;
                                    foreach (Interpolable selected in currentlySelectedInterpolables)
                                        toSelect.AddRange(selected.keyframes.Where(k => k.Key < currentTime));
                                    SelectKeyframes(toSelect);
                                }
                            });
                            elements.Add(new LeafElement()
                            {
                                icon = _selectAllSprite,
                                text = "Select keyframes after cursor",
                                onClick = p =>
                                {
                                    List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
                                    float currentTime = _playbackTime % _duration;
                                    foreach (Interpolable selected in currentlySelectedInterpolables)
                                        toSelect.AddRange(selected.keyframes.Where(k => k.Key >= currentTime));
                                    SelectKeyframes(toSelect);
                                }
                            });
                            elements.Add(new LeafElement()
                            {
                                icon = _colorSprite,
                                text = "Color",
                                onClick = p =>
                                {
#if HONEYSELECT
                                    Studio.Studio.Instance.colorPaletteCtrl.visible = true;
                                    Studio.Studio.Instance.colorMenu.updateColorFunc = null;
                                    if (currentlySelectedInterpolables.Count == 1)
                                        Studio.Studio.Instance.colorMenu.SetColor(currentlySelectedInterpolables[0].color, UI_ColorInfo.ControlType.PickerRect);
                                    Studio.Studio.Instance.colorMenu.updateColorFunc = col =>
                                    {
                                        foreach (Interpolable interp in currentlySelectedInterpolables)
                                        {
                                            InterpolableDisplay disp = this._displayedInterpolables.Find(id => id.interpolable.obj == interp);
                                            interp.color = col;
                                            this.UpdateInterpolableColor(disp, col);
                                        }
                                    };
#elif KOIKATSU
                                    Studio.Studio.Instance.colorPalette.visible = false;
                                    Studio.Studio.Instance.colorPalette.Setup("Interpolable Color", currentlySelectedInterpolables[0].color, (col) =>
                                    {
                                        foreach (Interpolable interp in currentlySelectedInterpolables)
                                        {
                                            InterpolableDisplay disp = _displayedInterpolables.Find(id => id.interpolable.obj == interp);
                                            interp.color = col;
                                            UpdateInterpolableColor(disp, col);
                                        }
                                    }, true);

#endif
                                }
                            });

                            elements.Add(new LeafElement()
                            {
                                icon = _addSprite,
                                text = currentlySelectedInterpolables.Count == 1 ? "Add keyframe at cursor" : "Add keyframes at cursor",
                                onClick = p =>
                                {
#if FEATURE_UNDO
                                    UndoPushAction();
#endif
                                    float time = _playbackTime % _duration;
                                    foreach (Interpolable selectedInterpolable in currentlySelectedInterpolables)
                                        AddKeyframe(selectedInterpolable, time);
                                    UpdateGrid();
                                }
                            });
                            var treeGroups = GetInterpolablesTreeGroups(currentlySelectedInterpolables.Select(elem => (INode)_interpolablesTree.GetLeafNode(elem)).ToList());
                            if (treeGroups.Count > 0)
                            {
                                elements.Add(new GroupElement()
                                {
                                    icon = _addToFolderSprite,
                                    text = "Parent to",
                                    elements = treeGroups
                                });
                            }
                            elements.Add(new LeafElement()
                            {
                                icon = _checkboxSprite,
                                text = currentlySelectedInterpolables.Count == 1 ? "Disable" : "Disable all",
                                onClick = p =>
                                {
#if FEATURE_UNDO
                                    UndoPushAction();
#endif
                                    foreach (Interpolable selectedInterpolable in currentlySelectedInterpolables)
                                        selectedInterpolable.enabled = false;
                                    UpdateInterpolablesView();
                                }
                            });
                            elements.Add(new LeafElement()
                            {
                                icon = _checkboxCompositeSprite,
                                text = currentlySelectedInterpolables.Count == 1 ? "Enable" : "Enable all",
                                onClick = p =>
                                {
#if FEATURE_UNDO
                                    UndoPushAction();
#endif
                                    foreach (Interpolable selectedInterpolable in currentlySelectedInterpolables)
                                        selectedInterpolable.enabled = true;
                                    UpdateInterpolablesView();
                                }
                            });
                            elements.Add(new LeafElement()
                            {
                                icon = _chevronUpSprite,
                                text = "Move up",
                                onClick = p =>
                                {
#if FEATURE_UNDO
                                    UndoPushAction();
#endif
                                    _interpolablesTree.MoveUp(currentlySelectedInterpolables.Select(elem => (INode)_interpolablesTree.GetLeafNode(elem)));
                                    UpdateInterpolablesView();
                                }
                            });
                            elements.Add(new LeafElement()
                            {
                                icon = _chevronDownSprite,
                                text = "Move down",
                                onClick = p =>
                                {
#if FEATURE_UNDO
                                    UndoPushAction();
#endif
                                    _interpolablesTree.MoveDown(currentlySelectedInterpolables.Select(elem => (INode)_interpolablesTree.GetLeafNode(elem)));
                                    UpdateInterpolablesView();
                                }
                            });
                            elements.Add(new LeafElement()
                            {
                                icon = _deleteSprite,
                                text = "Delete",
                                onClick = p =>
                                {
                                    string message = currentlySelectedInterpolables.Count > 1
                                            ? "Are you sure you want to delete these Interpolables?"
                                            : "Are you sure you want to delete this Interpolable?";
                                    UIUtility.DisplayConfirmationDialog(result =>
                                    {
                                        if (result) {
#if FEATURE_UNDO
                                            UndoPushAction();
#endif
                                            RemoveInterpolables(currentlySelectedInterpolables);
                                        }
                                    }, message);
                                }
                            });
                            UIUtility.ShowContextMenu(_ui, localPoint, elements, 220);
                        }
                        break;
                    }
                };
            
            return display;
        }
#endif

        private void UpdateGridMaterial()
        {
            _gridImage.material.SetFloat("_TilingX", _duration / 10f);
            _gridImage.material.SetFloat("_BlockLength", _blockLength);
            _gridImage.material.SetFloat("_Divisions", _divisions);
            _gridImage.enabled = false;
            _gridImage.enabled = true;
        }

        private void SelectAddKeyframes(params KeyValuePair<float, Keyframe>[] keyframes)
        {
            SelectAddKeyframes((IEnumerable<KeyValuePair<float, Keyframe>>)keyframes);
        }

        private void SelectAddKeyframes(IEnumerable<KeyValuePair<float, Keyframe>> keyframes)
        {
            foreach (KeyValuePair<float, Keyframe> keyframe in keyframes)
            {
                int index = _selectedKeyframes.FindIndex(k => k.Value == keyframe.Value);
                if (index != -1)
                    _selectedKeyframes.RemoveAt(index);
                else
                    _selectedKeyframes.Add(keyframe);
            }
            _keyframeSelectionSize = _selectedKeyframes.Count < 2 ? 0 : _selectedKeyframes.Max(k => k.Key) - _selectedKeyframes.Min(k => k.Key);
            UpdateKeyframeSelection();
            UpdateKeyframeWindow();
        }

        private void SelectKeyframes(params KeyValuePair<float, Keyframe>[] keyframes)
        {
            SelectKeyframes((IEnumerable<KeyValuePair<float, Keyframe>>)keyframes);
        }

        private void SelectKeyframes(IEnumerable<KeyValuePair<float, Keyframe>> keyframes)
        {
            _selectedKeyframes.Clear();
            if (keyframes.Count() != 0)
                SelectAddKeyframes(keyframes);
            else
                CloseKeyframeWindow();
        }

        private void UpdateKeyframeSelection()
        {
            ObjectCtrlItem objectCtrlItem = GetOciControlInfo(_selectedOCI);

            if (objectCtrlItem != null)
            {
                foreach (KeyframeDisplay display in objectCtrlItem.displayedKeyframes)
                    display.image.color = _selectedKeyframes.Any(k => k.Value == display.keyframe) ? Color.green : Color.red;
            }
        }

        private void ScaleKeyframeSelection(float scrollDelta)
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
            {
                if (pair.Key < min)
                    min = pair.Key;
                if (pair.Key > max)
                    max = pair.Key;
            }
            if (Mathf.Approximately(min, max))
                return;
            double currentSize = max - min;

            double newSize;
            bool conflicting;
            int multiplier = 1;
            do
            {
                conflicting = false;
                double sizeMultiplier = Math.Round(Math.Round(currentSize * 10) / _keyframeSelectionSize + multiplier * (scrollDelta > 0 ? 1 : -1)) / 10;
                bool clamped = false;
                if (sizeMultiplier < 0.1)
                {
                    clamped = true;
                    sizeMultiplier = 0.1;
                }
                newSize = sizeMultiplier * _keyframeSelectionSize;
                foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                {
                    float newTime = (float)(((pair.Key - min) * newSize) / currentSize + min);
                    if (pair.Value.parent.keyframes.TryGetValue(newTime, out Keyframe otherKeyframe) && otherKeyframe != pair.Value)
                    {
                        conflicting = true;
                        ++multiplier;
                        break;
                    }
                }
                if (clamped && conflicting)
                    return;
            } while (conflicting);
#if FEATURE_UNDO
            UndoPushAction();
#endif
            for (int i = 0; i < _selectedKeyframes.Count; i++)
            {
                KeyValuePair<float, Keyframe> pair = _selectedKeyframes[i];
                float newTime = (float)(((pair.Key - min) * newSize) / currentSize + min);
                MoveKeyframe(pair.Value, newTime);
                _selectedKeyframes[i] = new KeyValuePair<float, Keyframe>(newTime, pair.Value);
            }

            UpdateKeyframeWindow(false);
            UpdateGrid();
        }

        private void OnKeyframeContainerMouseDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Middle && RectTransformUtility.ScreenPointToLocalPointInRectangle(_keyframesContainer, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                float time = 10f * localPoint.x / (_baseGridWidth * _zoomLevel);
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    float beat = _blockLength / _divisions;
                    float mod = time % beat;
                    if (mod / beat > 0.5f)
                        time += beat - mod;
                    else
                        time -= mod;
                }

                if (Input.GetKey(KeyCode.LeftControl) && Input.GetKey(KeyCode.LeftAlt))
                {
#if FEATURE_AUTOGEN
                    StartCoroutine(GenerateAnimationTimeline(time));
#endif
                }
                else
                {
                    if (Input.GetKey(KeyCode.LeftAlt) && _selectedInterpolables.Count != 0)
                    {
#if FEATURE_UNDO
                        UndoPushAction();
#endif
                        foreach (Interpolable selectedInterpolable in _selectedInterpolables)
                            AddKeyframe(selectedInterpolable, time);
                        
                        UpdateGrid();
                    }
                    else
                    {
                        if (_selectedInterpolables.Count != 0)
                            ClearSelectedInterpolables();
                        InterpolableModel model = null;
                        float distance = float.MaxValue;
                        foreach (InterpolableDisplay display in _displayedInterpolables)
                        {
                            if (!display.gameObject.activeSelf)
                                continue;
                            float distance2 = Mathf.Abs(localPoint.y - ((RectTransform)display.gameObject.transform).anchoredPosition.y);
                            if (distance2 < distance)
                            {
                                distance = distance2;
                                model = display.interpolable.obj;
                            }
                        }
                        foreach (InterpolableModelDisplay display in _displayedInterpolableModels)
                        {
                            if (!display.gameObject.activeSelf)
                                continue;
                            float distance2 = Mathf.Abs(localPoint.y - ((RectTransform)display.gameObject.transform).anchoredPosition.y);
                            if (distance2 < distance)
                            {
                                distance = distance2;
                                model = display.model;
                            }
                        }
                        if (model != null)
                        {
                            Interpolable interpolable;

                            if (model is Interpolable)
                                interpolable = (Interpolable)model;
                            else
                                interpolable = AddInterpolable(model);

                            if (interpolable != null)
                            {
#if FEATURE_SOUND
                                if (interpolable.instantAction) {
                                    _keepSoundInterpolable = new KeyValuePair<float, Interpolable>(time, interpolable); 
                                    ToggleSingleFiles("*.wav");                                 
                                } else {
#if FEATURE_UNDO
                                    UndoPushAction();
#endif
                                    AddKeyframe(interpolable, time);
                                    UpdateGrid();
                                }
#else

#if FEATURE_UNDO
                                UndoPushAction();
#endif
                                AddKeyframe(interpolable, time);
                                UpdateGrid();
#endif
                            }
                        }
                    }
                }
            }
        }

#if FEATURE_AUTOGEN
        private IEnumerator GenerateAnimationTimeline(float startTime)
        {   
            List<Interpolable> enabledInterpolables = GetAllInterpolables(true).ToList(); 
            OCIChar _character = null;
            if (_selectedOCI != null)
                _character = (OCIChar)_selectedOCI;

            if (_character != null && enabledInterpolables.Count > 0) {
                
                if (isAutoGenerating) {     
                    isAutoGenerating = false;               
                } else {                    
#if FEATURE_UNDO
                    UndoPushAction();
#endif
                    Pause();

                    float _keyframe = 0.0f;
                    int _animation_cnt = 0;
                    List<object> prevInterpolableValues = new List<object>();

                    Studio.Studio.Instance.manipulatePanelCtrl.active = true;
                    Studio.Studio.Instance.manipulatePanelCtrl.charaPanelInfo.mpCharCtrl.fkInfo.ociChar = _character;

                    Action reflectAnimationToFKIK = DoNothing;     
                    bool isFKIK = false; 

                    if (_character.oiCharInfo.enableFK && _character.oiCharInfo.enableIK) {
                       isFKIK = true;                
                       reflectAnimationToFKIK = DoFKIK;
                    } else if (_character.oiCharInfo.enableFK && !_character.oiCharInfo.enableIK) {
                       reflectAnimationToFKIK = DoFK;
                    } else if (!_character.oiCharInfo.enableFK && _character.oiCharInfo.enableIK) {
                       reflectAnimationToFKIK = DoIK;
                    }
                    
                    OICharInfo.AnimeInfo _aniInfo = _character.oiCharInfo.animeInfo;                  
                    Animator _animator = _character.charAnimeCtrl.animator;

                    // speed set to none-zero
                    AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);

                    if (stateInfo.loop == true) {                        
                        Logger.LogMessage($"Start Generating for loop");
                       
                        isAutoGenerating = true;
                    
                        List<KeyValuePair<float, Keyframe>> generatedKeyPairs = new List<KeyValuePair<float, Keyframe>>();

                        Stopwatch stopWatch = new Stopwatch();
                        Stopwatch idleWatch = new Stopwatch();
                                            
                        reflectAnimationToFKIK();  
                        yield return new WaitForEndOfFrame();
                        
                        reflectAnimationToFKIK();         
                        yield return new WaitForEndOfFrame();

                        // 초기 프레임 저장 및 이전 상태 생성
                        prevInterpolableValues.AddRange(GetCurrentValueFromInterpolables(enabledInterpolables));
                        generatedKeyPairs.AddRange(AddKeyframes(enabledInterpolables, startTime));

                        while (isAutoGenerating)
                        {
                            Studio.Studio.Instance.manipulatePanelCtrl.active = true;

                            reflectAnimationToFKIK(); 
                            yield return new WaitForEndOfFrame();

                            if (_animation_cnt % 5 == 0) {
                                if (stopWatch.ElapsedMilliseconds > 0.0f) {
                                    _keyframe = startTime + stopWatch.ElapsedMilliseconds/1000.0f;

                                    int genCount = generatedKeyPairs.Count;
                                    for (int idx=0; idx < enabledInterpolables.Count; idx++) {    

                                        object a1 = enabledInterpolables[idx].GetValue();
                                        object a2 = prevInterpolableValues[idx];
                                        
                                        if (a1 is System.Single[] arr && a2 is System.Single[] arrOther) {
                                            if (!arr.SequenceEqual(arrOther)) {
                                                generatedKeyPairs.Add(AddKeyframe(enabledInterpolables[idx], _keyframe));
                                                prevInterpolableValues[idx] = a1;    
                                            }
                                        } else {
                                            if (!a1.Equals(a2)) {
                                                generatedKeyPairs.Add(AddKeyframe(enabledInterpolables[idx], _keyframe));
                                                prevInterpolableValues[idx] = a1;    
                                            }
                                        }
                                    }
                                    
                                    if (idleWatch.ElapsedMilliseconds == 0.0f || genCount < generatedKeyPairs.Count) {
                                        idleWatch.Reset();
                                        idleWatch.Start();
                                    }

                                    if (idleWatch.ElapsedMilliseconds/1000.0f > 3.5) {
                                        isAutoGenerating = false;
                                    }
                                } else {
                                    for (int idx=0; idx < enabledInterpolables.Count; idx++) {    

                                        object a1 = enabledInterpolables[idx].GetValue();
                                        object a2 = prevInterpolableValues[idx];                            

                                        if (a1 is System.Single[] arr && a2 is System.Single[] arrOther) {
                                            if (!arr.SequenceEqual(arrOther)) {
                                                stopWatch.Start();
                                                break;    
                                            }
                                        } else {
                                            if (!a1.Equals(a2)) {
                                                stopWatch.Start();
                                                break;    
                                            }
                                        }
                                    }
                                }
                                UpdateGrid();
                            }
                            
                            _animation_cnt++;
                        }
                    
                        idleWatch.Stop();
                        stopWatch.Stop();
                                            
                    } else {                
                        // Logger.LogMessage($"anim {_aniInfo.category}, {_aniInfo.group}, {_aniInfo.no}");    
                        if ((_character.oiCharInfo.enableFK && !_character.oiCharInfo.enableIK) || (_aniInfo.category == 0 && _aniInfo.group == 0 &&  _aniInfo.no == 0)) { 
                            Logger.LogMessage($"Start Generating for pose");    
                            isAutoGenerating = true;
                        
                            // pose captured                                            
                            prevInterpolableValues.AddRange(GetLastPrevKeyframesFromInterpolables(enabledInterpolables, startTime));
                            
                            for (int idx=0; idx < enabledInterpolables.Count; idx++) {     
                                object a1 = enabledInterpolables[idx].GetValue();
                                object a2 = prevInterpolableValues[idx];
                                
                                if (a1 is System.Single[] arr && a2 is System.Single[] arrOther) {
                                    if (!arr.SequenceEqual(arrOther)) {
                                        AddKeyframe(enabledInterpolables[idx], _keyframe); 
                                    }
                                } else {
                                    if (!a1.Equals(a2)) {
                                        AddKeyframe(enabledInterpolables[idx], _keyframe);
                                    }
                                }
                            }
                        } else {
                            isAutoGenerating = true;
                            Logger.LogMessage($"Start Generating for onetime");
                            
                            float _currentNormalizedTime = 0.0f;
                                
                            // 캐릭터 기본 애니메이션 ik, fk 녹화
                            _animator.Play(stateInfo.shortNameHash, 0, 0f);
                            _animator.Update(0); // 애니메이션을 강제로 수행시켜, play 상태를 강제로 반영

                            // 초기 프레임 저장     
                            reflectAnimationToFKIK();        
                            yield return new WaitForEndOfFrame();

                            // 초기 프레임 저장 및 이전 상태 생성
                            prevInterpolableValues.AddRange(GetCurrentValueFromInterpolables(enabledInterpolables));
                            AddKeyframes(enabledInterpolables, startTime);
                            UpdateGrid();

                            while (isAutoGenerating)
                            {
                                reflectAnimationToFKIK();
                                yield return new WaitForEndOfFrame();                                
                                
                                Studio.Studio.Instance.manipulatePanelCtrl.active = true;
                                stateInfo = _animator.GetCurrentAnimatorStateInfo(0);  

                                if ((stateInfo.normalizedTime / 1.0f) >= 1.0f) {
                                    _keyframe = startTime + 1.0f * stateInfo.length; 
                                    
                                    for (int idx=0; idx < enabledInterpolables.Count; idx++) {     

                                        object a1 = enabledInterpolables[idx].GetValue();
                                        object a2 = prevInterpolableValues[idx];
                                        
                                        if (a1 is System.Single[] arr && a2 is System.Single[] arrOther) {
                                            if (!arr.SequenceEqual(arrOther)) {
                                                AddKeyframe(enabledInterpolables[idx], _keyframe); 
                                            }
                                        } else {
                                            if (!a1.Equals(a2)) {
                                                AddKeyframe(enabledInterpolables[idx], _keyframe);
                                            }
                                        }
                                    }                                    
                                    break;
                                } else {
                                    if (_animation_cnt % 5 == 0) {                                    
                                        _currentNormalizedTime = stateInfo.normalizedTime % 1.0f;
                                        _keyframe = startTime + _currentNormalizedTime * stateInfo.length;                      

                                        for (int idx=0; idx < enabledInterpolables.Count; idx++) {           
                                            object a1 = enabledInterpolables[idx].GetValue();
                                            object a2 = prevInterpolableValues[idx];
                                            
                                            if (a1 is System.Single[] arr && a2 is System.Single[] arrOther) {
                                                if (!arr.SequenceEqual(arrOther)) {
                                                    AddKeyframe(enabledInterpolables[idx], _keyframe);
                                                    prevInterpolableValues[idx] = a1; 
                                                }
                                            } else {
                                                if (!a1.Equals(a2)) {
                                                    AddKeyframe(enabledInterpolables[idx], _keyframe);
                                                    prevInterpolableValues[idx] = a1;    
                                                }
                                            }
                                        }
                                        UpdateGrid();
                                    }

                                    _animation_cnt++;
                                }
                            }                                
                        }
                    }                                                                 
                    // }
                   
                    isAutoGenerating = false;
                    UpdateGrid();                    
                    CloseKeyframeWindow();            

                    Logger.LogMessage($"End Generating");                    
                }
            }
        }
        
        private void DoNothing() {
        }

        private void DoFKIK() {
            OCIChar character = _selectedOCI as OCIChar;
            SetCopyBoneFK(character, (OIBoneInfo.BoneGroup)353);
            SetCopyBoneIK(character, (OIBoneInfo.BoneGroup)31);
        }

        private void DoFK() {        
            OCIChar character = _selectedOCI as OCIChar;
            SetCopyBoneFK(character, (OIBoneInfo.BoneGroup)353);
        }

        private void DoIK() {                    
            OCIChar character = _selectedOCI as OCIChar;
            SetCopyBoneIK(character, (OIBoneInfo.BoneGroup)31);
        }

        private void SetCopyBoneFK(OCIChar ociChar, OIBoneInfo.BoneGroup _group)
		{         
			SingleAssignmentDisposable _disposableFK = new SingleAssignmentDisposable();
			_disposableFK.Disposable = this.LateUpdateAsObservable().Take(1).Subscribe(delegate(Unit _)
			{
				ociChar.fkCtrl.CopyBone(_group);
			}, delegate()
			{
				_disposableFK.Dispose();
			});
		}

		private void SetCopyBoneIK(OCIChar ociChar, OIBoneInfo.BoneGroup _group)
		{
			SingleAssignmentDisposable _disposableIK = new SingleAssignmentDisposable();
			_disposableIK.Disposable = this.LateUpdateAsObservable().Take(1).Subscribe(delegate(Unit _)
			{
                ociChar.ikCtrl.CopyBone(_group);
			}, delegate()
			{
				_disposableIK.Dispose();         
			});
		}
#endif

#if FEATURE_AUTOGEN
        // startTime 기준 각 interpolables에 대한 이전 keyframe 정보 획득
        private List<object> GetLastPrevKeyframesFromInterpolables(List<Interpolable> interpolables, float startTime) {
            List<object> valueList = new List<object>(); 

            foreach (Interpolable interpolable in interpolables) {
                    var keyframeObj = interpolable.keyframes
                        .Where(kvp => kvp.Key < startTime)
                        .OrderByDescending(kvp => kvp.Key)
                        .FirstOrDefault();
                          
                    Keyframe keyframe = (Keyframe)keyframeObj.Value;
                    valueList.Add(keyframe.value);
            }
            return valueList;
        }

        // 각 interpolables에 대한 현재 keyframe 정보 획득
        private List<object> GetCurrentValueFromInterpolables(List<Interpolable> interpolables) {
            List<object> valueList = new List<object>(); 

            foreach (Interpolable interpolable in interpolables) {
                valueList.Add(interpolable.GetValue());
            }

            return valueList;
        }

#if FEATURE_AUTOGEN
        private void SelectAllAction() {
            if (_selectedKeyframes.Count > 0)
                _selectedKeyframes.Clear();
            else {
                List<Interpolable> enabledInterpolables = GetAllInterpolables(true).ToList(); 
                foreach (Interpolable interpolable in enabledInterpolables) {
                    foreach (KeyValuePair<float, Keyframe> pair in interpolable.keyframes){
                        if (pair.Key != 0.099f)
                            _selectedKeyframes.Add(pair);
                    }
                }
            }
            UpdateKeyframeWindow(false);
            UpdateGrid();
        }

        private void DeleteAllAction() {
            if (_selectedKeyframes.Count > 0) {
                DeleteSelectedKeyframes();
            }
            else {
                Logger.LogMessage("Nothing to be deleted");
            }
        }

        private void MoveLeftKeyframes()
        {
            if(_selectedKeyframes.Count > 0) {
#if FEATURE_UNDO
                UndoPushAction();
#endif
                float _interval = 0.1f;
                for (int i = 0; i < _selectedKeyframes.Count; i++)
                    {
                        KeyValuePair<float, Keyframe> pair = _selectedKeyframes[i];
                        float newTime = (float)(pair.Key - _interval);
                        MoveKeyframe(pair.Value, newTime);
                        _selectedKeyframes[i] = new KeyValuePair<float, Keyframe>(newTime, pair.Value);
                    }
                UpdateKeyframeWindow(false);
                UpdateGrid();
            }
        }

        private void MoveRightKeyframes()
        {
            if(_selectedKeyframes.Count > 0) {
#if FEATURE_UNDO
                UndoPushAction();
#endif
                float _interval = 0.1f;
                for (int i = 0; i < _selectedKeyframes.Count; i++)
                {
                    KeyValuePair<float, Keyframe> pair = _selectedKeyframes[i];
                    float newTime = (float)(pair.Key + _interval);
                    MoveKeyframe(pair.Value, newTime);
                    _selectedKeyframes[i] = new KeyValuePair<float, Keyframe>(newTime, pair.Value);
                }
                UpdateKeyframeWindow(false);
                UpdateGrid();
            }
        }
#endif

#if FEATURE_UNDO
        private TransactionData MakeAction() {
            StringBuilder sb = new StringBuilder();
            StringWriter stringWriter = new StringWriter(sb);
            XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter);

            List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();
            xmlWriter.WriteStartElement("root");
            foreach (INode node in _interpolablesTree.tree)
                WriteInterpolableTree(node, xmlWriter, dic, leafNode => (leafNode.obj.oci == _selectedOCI || leafNode.obj.useOciInHash == false));
            xmlWriter.WriteEndElement();
            xmlWriter.Flush();
            xmlWriter.Close();

            return new TransactionData(_selectedOCI, sb.ToString());
        } 

        private void UndoPushAction() {          
            if (ConfigKeyEnableUndoRedo.Value) {
#if FEATURE_UNDO_DEBUG
                Logger.LogMessage("UndoPushAction");
#endif                
                if (_undoStack.Count > 0) {
                    TransactionData transactionData = _undoStack.Peek();
                    if (transactionData.ctrlInfo != _selectedOCI) {
                        _undoStack.Clear();
                    }
                }
                
                _redoStack.Clear();
                _undoStack.Push(MakeAction());       
            }
        }

        private void UndoPopupAction(int type)   // type 0: undo, type 1: redo
        {   
            if (ConfigKeyEnableUndoRedo.Value) {
                TransactionData transactionData = null;

                if (_selectedOCI == null) {
                    return;
                }

                if (type == 0) {
                    if (_undoStack.Count > 0) {
                        transactionData = _undoStack.Peek();
                    }
                }
                else { 
                    if (_redoStack.Count > 0) {
                        transactionData = _redoStack.Peek();
                    }
                }

                if (transactionData != null) {

                    if (transactionData.ctrlInfo != _selectedOCI) {
                        return;
                    }

                    XmlDocument doc = new XmlDocument();
                    try
                    {
                        doc.LoadXml(transactionData.data);
               
                        // 이전 interpolable 정보 clean
                        _selectedInterpolables.Clear();
                        _selectedKeyframes.Clear();

                        List<Interpolable> temp_oci_Interpolables = new List<Interpolable>();
                        foreach (KeyValuePair<int, Interpolable> pair in _interpolables) {
                            if(pair.Value.oci == _selectedOCI) {
                                temp_oci_Interpolables.Add(pair.Value);
                            }
                        }   

                        foreach (Interpolable interpolable in temp_oci_Interpolables) {
                            RemoveInterpolableUndo(interpolable);
                        }                        

                        // 신규 interpolable 정보 update
                        ReadInterpolableTree(doc.FirstChild, new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList(), _selectedOCI);
       
                        if (type == 0) {     
                            _redoStack.Push(_undoStack.Pop());
                        }
                        else { 
                            _undoStack.Push(_redoStack.Pop());
                        }
                    }
                    catch (Exception ex)
                    {
                        // Console.WriteLine("exception " + ex.Message);
                    }

#if FEATURE_UNDO_DEBUG 
                    Logger.LogMessage("UndoPopupAction");
#endif                    
                    UpdateKeyframeWindow(false);
                    UpdateInterpolablesView();
                } 
            }
        }
#endif
        private List<KeyValuePair<float, Keyframe>>  AddKeyframes(List<Interpolable> interpolables, float time)
        {
            Keyframe keyframe;
            List<KeyValuePair<float, Keyframe>> keyframes = new List<KeyValuePair<float, Keyframe>>();
            foreach (Interpolable interpolable in interpolables) {
                try
                {
                    KeyValuePair<float, Keyframe> pair = interpolable.keyframes.LastOrDefault(k => k.Key < time);
                    if (pair.Value != null)
                        keyframe = new Keyframe(interpolable.GetValue(), interpolable, new AnimationCurve(pair.Value.curve.keys));
                    else
                        keyframe = new Keyframe(interpolable.GetValue(), interpolable, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                        
                    interpolable.keyframes.Add(time, keyframe);
                    keyframes.Add(new KeyValuePair<float, Keyframe>(time, keyframe));
                }
                catch (Exception e)
                {
                    Logger.LogError("couldn't add keyframe to interpolable with value:" + interpolable + "\n" + e);
                }
            }

            return keyframes;
        }
#endif

#if FEATURE_SOUND
        private KeyValuePair<float, Keyframe> AddSoundToKeyframe(float time, Interpolable interpolable, SoundItem SoundItem)
        {
            Keyframe keyframe;
            KeyValuePair<float, Keyframe> keyValuePair = new KeyValuePair<float, Keyframe>(0f, null);
            try
            {
                KeyValuePair<float, Keyframe> pair = interpolable.keyframes.LastOrDefault(k => k.Key < time);
                if (pair.Value != null)
                    keyframe = new Keyframe(SerializedSoundItem(SoundItem), interpolable, new AnimationCurve(pair.Value.curve.keys));
                else
                    keyframe = new Keyframe(SerializedSoundItem(SoundItem), interpolable, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                interpolable.keyframes.Add(time, keyframe);
                keyValuePair = new KeyValuePair<float, Keyframe>(time, keyframe);
            }
            catch (Exception e)
            {
                Logger.LogError("couldn't add keyframe to interpolable with value:" + interpolable + "\n" + e);
            }

            return keyValuePair;
        }

        private string SerializedSoundItem(SoundItem sfx)
        {
            return JsonUtility.ToJson(sfx);
        }
#endif
        private KeyValuePair<float, Keyframe> AddKeyframe(Interpolable interpolable, float time)
        {
            Keyframe keyframe;
            KeyValuePair<float, Keyframe> keyValuePair = new KeyValuePair<float, Keyframe>(0f, null);
            try
            {
                KeyValuePair<float, Keyframe> pair = interpolable.keyframes.LastOrDefault(k => k.Key < time);
                if (pair.Value != null)
                    keyframe = new Keyframe(interpolable.GetValue(), interpolable, new AnimationCurve(pair.Value.curve.keys));
                else
                    keyframe = new Keyframe(interpolable.GetValue(), interpolable, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                interpolable.keyframes.Add(time, keyframe);
                keyValuePair = new KeyValuePair<float, Keyframe>(time, keyframe);
            }
            catch (Exception e)
            {
                Logger.LogError("couldn't add keyframe to interpolable with value:" + interpolable + "\n" + e);
            }

            return keyValuePair;
        }

        private void CopyKeyframes()
        {
            _copiedKeyframes.Clear();
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                _copiedKeyframes.Add(new KeyValuePair<float, Keyframe>(pair.Key, new Keyframe(pair.Value)));
        }

        private void CutKeyframes()
        {
            CopyKeyframes();
            if (_selectedKeyframes.Count != 0)
            {
#if FEATURE_UNDO
                UndoPushAction();
#endif
                DeleteKeyframes(_selectedKeyframes, false);
            }
        }

        private void PasteKeyframes()
        {
            if (_copiedKeyframes.Count == 0)
                return;

#if FEATURE_UNDO
            UndoPushAction();
#endif
            List<KeyValuePair<float, Keyframe>> toSelect = new List<KeyValuePair<float, Keyframe>>();
            float time = _playbackTime % _duration;
            if (time == 0f && _playbackTime == _duration)
                time = _duration;
            float startOffset = _copiedKeyframes.Min(k => k.Key);
            if (Input.GetKey(KeyCode.LeftAlt))
            {
                float max = _copiedKeyframes.Max(k => k.Key);
                //// If they keyframe(s) that are at the end of the selection would conflict with any pushed keyframes (those that are currently on the cursor), then cancel
                //if (this._copiedKeyframes.Where(k => Mathf.Approximately(k.Key, max)).Any(k => k.Value.parent.keyframes.ContainsKey(time)))
                //    return;
                double duration = max - startOffset + (_blockLength / _divisions);
                foreach (IGrouping<Interpolable, KeyValuePair<float, Keyframe>> group in _copiedKeyframes.GroupBy(k => k.Value.parent))
                {
                    foreach (KeyValuePair<float, Keyframe> pair in @group.Key.keyframes.Reverse())
                    {
                        if (pair.Key >= time)
                            MoveKeyframe(pair.Value, (float)(pair.Key + duration));
                    }
                }
            }
            else if (_copiedKeyframes.Any(k => k.Value.parent.keyframes.ContainsKey(time + k.Key - startOffset)))
                return;
            foreach (KeyValuePair<float, Keyframe> pair in _copiedKeyframes)
            {
                float finalTime = time + pair.Key - startOffset;
                Keyframe newKeyframe = new Keyframe(pair.Value);
                pair.Value.parent.keyframes.Add(finalTime, newKeyframe);
                // This is dumb as shit but I have no choice
                toSelect.Add(new KeyValuePair<float, Keyframe>(finalTime, newKeyframe));
            }

            SelectKeyframes(toSelect);
            UpdateGrid();
        }

        private void MoveKeyframe(Keyframe keyframe, float destinationTime)
        {
            Logger.LogError(keyframe.parent.keyframes.IndexOfValue(keyframe));
            keyframe.parent.keyframes.RemoveAt(keyframe.parent.keyframes.IndexOfValue(keyframe));
            keyframe.parent.keyframes.Add(destinationTime, keyframe);
            int i = _selectedKeyframes.FindIndex(k => k.Value == keyframe);
            if (i != -1)
                _selectedKeyframes[i] = new KeyValuePair<float, Keyframe>(destinationTime, keyframe);
        }

        private void OnGridTopMouse(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && RectTransformUtility.ScreenPointToLocalPointInRectangle(_gridTop, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                float time = 10f * localPoint.x / (_baseGridWidth * _zoomLevel);
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    float beat = _blockLength / _divisions;
                    float mod = time % beat;
                    if (mod / beat > 0.5f)
                        time += beat - mod;
                    else
                        time -= mod;
                }
                time = Mathf.Clamp(time, 0, _duration);
                SeekPlaybackTime(time);
            }
        }

        private void OnResizeWindow(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && RectTransformUtility.ScreenPointToLocalPointInRectangle(_timelineWindow, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                localPoint.x = Mathf.Clamp(localPoint.x, 615f, ((RectTransform)_ui.transform).rect.width * 0.85f);
                localPoint.y = Mathf.Clamp(localPoint.y, 330f, ((RectTransform)_ui.transform).rect.height * 0.85f);
                _timelineWindow.sizeDelta = localPoint;
            }
        }

        private void SeekPlaybackTime(float t)
        {
            if (t == _playbackTime)
                return;
            _playbackTime = t;
            _startTime = Time.time - _playbackTime;
            bool isPlaying = _isPlaying;
            _isPlaying = true;
            UpdateCursor();
            Interpolate(true);
            Interpolate(false);
            _isPlaying = isPlaying;
        }

        private void ToggleSingleFilesPanel()
        {
            ToggleSingleFiles("*.xml");
        }

        private void ToggleSingleFiles(string format = "*.xml")
        {
            _singleFilesPanel.SetActive(!_singleFilesPanel.activeSelf);
            if (_singleFilesPanel.activeSelf)
            {
                bool isActive = true;

                if (format == "*.wav")
                {
                    isActive = false;
                }

                _singleFileNameField.text = "";
                _singleFilesPanel.transform.Find("Main Container/Buttons/Save").gameObject.SetActive(isActive);
                _singleFilesPanel.transform.Find("Main Container/Buttons/Delete").gameObject.SetActive(isActive);
                UpdateSingleFilesPanel(format);
            }
        }

        private void UpdateSingleFilesPanel(string file_format = "*.xml")
        {
#if FEATURE_SOUND
            string singleFilesFolder = _singleFilesFolder;
            string[] files;

            if (file_format == "*.wav")
            {
                singleFilesFolder = singleFilesFolder + "\\sfx";          
                
                if (Directory.Exists(singleFilesFolder) == false)
                    return;
                    
                files =  Directory
                    .GetFiles(singleFilesFolder, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".wav") || f.EndsWith(".ogg"))
                    .ToArray();
            } else {
                if (Directory.Exists(singleFilesFolder) == false)
                    return;
                
                files = Directory.GetFiles(_singleFilesFolder, file_format);
            }

#else
            if (Directory.Exists(_singleFilesFolder) == false)
                return;
            string[] files = Directory.GetFiles(_singleFilesFolder, file_format);
#endif
            int i = 0;
            for (; i < files.Length; i++)
            {
                SingleFileDisplay display;
                if (i < _displayedSingleFiles.Count)
                    display = _displayedSingleFiles[i];
                else
                {
                    display = new SingleFileDisplay();
                    display.toggle = GameObject.Instantiate(_singleFilePrefab).GetComponent<Toggle>();
                    display.toggle.gameObject.hideFlags = HideFlags.None;
                    display.text = display.toggle.GetComponentInChildren<Text>();

                    display.toggle.transform.SetParent(_singleFilesContainer);
                    display.toggle.transform.localScale = Vector3.one;
                    display.toggle.transform.localPosition = Vector3.zero;
                    display.toggle.group = _singleFilesContainer.GetComponent<ToggleGroup>();
                    _displayedSingleFiles.Add(display);
                }
#if FEATURE_SOUND
                string fileName = Path.GetFileName(files[i]);
#else
                string fileName = Path.GetFileNameWithoutExtension(files[i]);
#endif
                display.toggle.gameObject.SetActive(true);
                display.toggle.onValueChanged = new Toggle.ToggleEvent();
                display.toggle.onValueChanged.AddListener(b =>
                {
                    if (display.toggle.isOn)
                        _singleFileNameField.text = fileName;
                });
                display.text.text = fileName;
            }

            for (; i < _displayedSingleFiles.Count; ++i)
                _displayedSingleFiles[i].toggle.gameObject.SetActive(false);
            UpdateSingleFileSelection();
        }

        private void UpdateSingleFileSelection()
        {
            foreach (SingleFileDisplay display in _displayedSingleFiles)
            {
                if (display.toggle.gameObject.activeSelf == false)
                    break;
                display.toggle.isOn = string.Compare(_singleFileNameField.text, display.text.text, StringComparison.OrdinalIgnoreCase) == 0;
            }
        }

        private void LoadSingleFile()
        {
            try
            {
                if (_selectedOCI == null)
                {
                    Logger.LogMessage("Can't load: No studio object is selected. This function loads timeline data for a single studio object.");
                    return;
                }

#if FEATURE_SOUND
                string singleFilesFolder = _singleFilesFolder;
                if (!_singleFileNameField.text.Contains(".xml"))
                {
                    singleFilesFolder = singleFilesFolder + "\\sfx";
                }
                string path = Path.Combine(singleFilesFolder, _singleFileNameField.text);
#else
                string path = Path.Combine(_singleFilesFolder, _singleFileNameField.text + ".xml");
#endif
                // UnityEngine.Debug.Log($"path {path}");
                if (File.Exists(path))
                {
                    if (!path.Contains(".xml"))
                    {
#if FEATURE_SOUND
                        string soundFileName = _uuid + "_" + _singleFileNameField.text;
                        string soundFilePath = Path.Combine(Application.temporaryCachePath, soundFileName);

                        File.WriteAllBytes(soundFilePath, File.ReadAllBytes(path));

                        SoundItem SoundItem = new SoundItem();
                        // SoundItem.sceneName = _uuid + "";
                        SoundItem.fileName = soundFileName;
                        SoundItem.volume = 1.0f;

                        AddSoundToKeyframe(_keepSoundInterpolable.Key, _keepSoundInterpolable.Value, SoundItem);
                        UpdateGrid();
#endif
                    }
                    else
                    {
                        LoadSingle(path);
                        Logger.LogMessage("File was loaded successfully.");

#if FEATURE_AUTOGEN
                        _duration = 10.0f;
                        if (_singleFileNameField.text.Contains(AUTOGEN_INTERPOLABLE_FILE)) {                                               
                            _duration = 180.0f;
                        }
#if FEATURE_UNDO
                        _redoStack.Clear();
                        _undoStack.Clear();
#endif
#endif
                    }
                    ToggleSingleFiles();
                    UpdateGrid();
                }
                else
                {
                    Logger.LogMessage("Can't load: No file selected or the file no longer exists.");
                }
            }
            catch (Exception e)
            {
                Logger.LogMessage("Can't load: " + e.Message);
                Logger.LogError(e);
            }
        }

        private void SaveSingleFile()
        {
            try
            {
                if (_selectedOCI == null)
                {
                    Logger.LogMessage("Can't save: No studio object is selected. This function saves timeline data for a single studio object.");
                    return;
                }

                string selected = _singleFileNameField.text?.Trim();
                _singleFileNameField.text = selected;

                if (string.IsNullOrEmpty(selected) || selected.Intersect(Path.GetInvalidPathChars()).Any())
                {
                    Logger.LogMessage("Can't save: Provided name is empty or contains invalid characters.");
                    return;
                }

                if (Directory.Exists(_singleFilesFolder) == false)
                    Directory.CreateDirectory(_singleFilesFolder);

                string path = Path.Combine(_singleFilesFolder, selected + ".xml");

                SaveXmlSingle(path);

                UpdateSingleFilesPanel();

                Logger.LogMessage("File was saved successfully.");
            }
            catch (Exception e)
            {
                Logger.LogMessage("Can't save: " + e.Message);
                Logger.LogError(e);
            }
        }

        private void DeleteSingleFile()
        {
            try
            {
                string path = Path.Combine(_singleFilesFolder, _singleFileNameField.text + ".xml");
                if (File.Exists(path))
                {
                    UIUtility.DisplayConfirmationDialog(result =>
                    {
                        if (result)
                        {
                            File.Delete(path);
                            _singleFileNameField.text = "";
                            UpdateSingleFilesPanel();
                            Logger.LogMessage("File was deleted successfully.");
                        }
                    }, "Are you sure you want to delete this file?");
                }
                else
                {
                    Logger.LogMessage("Can't delete: No file selected or the file no longer exists.");
                }
            }
            catch (Exception e)
            {
                Logger.LogMessage("Can't delete: " + e.Message);
                Logger.LogError(e);
            }
        }
#if FEATURE_SOUND
        private void DeleteTemporaryCache()
        {
            string cachePath = Application.temporaryCachePath;

            if (!Directory.Exists(cachePath))
            {
                Logger.LogMessage("cache folder not exist: " + cachePath);
                return;
            }

            foreach (string file in Directory.GetFiles(cachePath))
            {
                File.Delete(file);
            }

            foreach (string dir in Directory.GetDirectories(cachePath))
            {
                Directory.Delete(dir, true);
            }          
        }

        private void CreateSoundToTemporaryCache(){

            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey2);
            if (data == null) {
                return;
            }

            // sound 생성 
            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneSoundInfo"]);

            XmlNodeList fileNodes = doc.GetElementsByTagName("file");

            foreach (XmlNode fileNode in fileNodes)
            {
                if (fileNode.Attributes != null)
                {
                    string name = fileNode.Attributes["name"]?.InnerText;
                    string base64Data = fileNode.Attributes["data"]?.InnerText;

                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(base64Data))
                    {
                        string soundFilePath = Path.Combine(Application.temporaryCachePath, name);
                        byte[] fileData = Convert.FromBase64String(base64Data);

                        File.WriteAllBytes(soundFilePath, fileData);
                    }
                }
            }
        }

#endif

        #endregion

        #region Keyframe Window
        private void OpenKeyframeWindow()
        {
            _keyframeWindow.gameObject.SetActive(true);
        }

        private void CloseKeyframeWindow()
        {
            _keyframeWindow.gameObject.SetActive(false);
            _selectedKeyframeCurvePointIndex = -1;
        }

        private void SelectPreviousKeyframe()
        {
            if (_selectedKeyframes.Count != 1)
                return;

            KeyValuePair<float, Keyframe> firstSelected = _selectedKeyframes[0];
            KeyValuePair<float, Keyframe> keyframe = firstSelected.Value.parent.keyframes.LastOrDefault(f => f.Key < firstSelected.Key);
            if (keyframe.Value != null)
                SelectKeyframes(keyframe);
        }

        private void SelectNextKeyframe()
        {
            if (_selectedKeyframes.Count != 1)
                return;

            KeyValuePair<float, Keyframe> firstSelected = _selectedKeyframes[0];
            KeyValuePair<float, Keyframe> keyframe = firstSelected.Value.parent.keyframes.FirstOrDefault(f => f.Key > firstSelected.Key);
            if (keyframe.Value != null)
                SelectKeyframes(keyframe);
        }

        private void UseCurrentTime()
        {
            float currentTime = _playbackTime % _duration;
            if (currentTime == 0f && _playbackTime == _duration)
                currentTime = _duration;
#if FEATURE_UNDO
            UndoPushAction();
#endif
            SaveKeyframeTime(currentTime);
            UpdateKeyframeTimeTextField();
        }

        private void DragAtCurrentTime()
        {
            if (_selectedKeyframes.Count == 0)
                return;

#if FEATURE_UNDO
            UndoPushAction();
#endif
            float currentTime = _playbackTime % _duration;
            if (currentTime == 0f && _playbackTime == _duration)
                currentTime = _duration;
            float min = _selectedKeyframes.Min(k => k.Key);

            // Checking if all keyframes can be moved.
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
            {
                Keyframe potentialDuplicateKeyframe;
                float time = currentTime + pair.Key - min;
                if (pair.Value.parent.keyframes.TryGetValue(time, out potentialDuplicateKeyframe) && potentialDuplicateKeyframe != pair.Value)
                    return;
            }

            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
            {
                pair.Value.parent.keyframes.Remove(pair.Key);
            }

            for (int i = 0; i < _selectedKeyframes.Count; i++)
            {
                KeyValuePair<float, Keyframe> pair = _selectedKeyframes[i];
                float time = currentTime + pair.Key - min;
                pair.Value.parent.keyframes.Add(time, pair.Value);
                _selectedKeyframes[i] = new KeyValuePair<float, Keyframe>(time, pair.Value);
            }

            UpdateKeyframeTimeTextField();
            this.ExecuteDelayed2(UpdateCursor2);
            UpdateGrid();
        }

        private void UpdateSelectedKeyframeTime(string s)
        {
            float time = ParseTime(_keyframeTimeTextField.text);
            if (time < 0)
                return;
#if FEATURE_UNDO
            UndoPushAction();
#endif
            SaveKeyframeTime(time);
        }

        private void UseCurrentValue()
        {
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                pair.Value.value = pair.Value.parent.GetValue();
            UpdateKeyframeValueText();
        }

        private void OnCurveMouseDown(PointerEventData eventData)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            if (eventData.button == PointerEventData.InputButton.Middle && Input.GetKey(KeyCode.LeftControl) == false && RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveContainer.rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
            {
                float time = localPoint.x / _curveContainer.rectTransform.rect.width;
                float value = localPoint.y / _curveContainer.rectTransform.rect.height;
                if (Input.GetKey(KeyCode.LeftShift))
                {
                    float mod = time % _curveGridCellSizePercent;
                    if (mod / _curveGridCellSizePercent > 0.5f)
                        time += _curveGridCellSizePercent - mod;
                    else
                        time -= mod;
                    mod = value % _curveGridCellSizePercent;
                    if (mod / _curveGridCellSizePercent > 0.5f)
                        value += _curveGridCellSizePercent - mod;
                    else
                        value -= mod;
                }
                UnityEngine.Keyframe curveKey = new UnityEngine.Keyframe(time, value);
                if (curveKey.time < 0 || curveKey.time > 1 || curveKey.value < 0 || curveKey.value > 1)
                    return;
                _selectedKeyframeCurvePointIndex = _selectedKeyframes[0].Value.curve.AddKey(curveKey);
                SaveKeyframeCurve();
                UpdateCurve();
            }
        }

        private void UpdateCurvePointTime(string s)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex >= 1 && _selectedKeyframeCurvePointIndex < curve.length - 1)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                float v;
                if (float.TryParse(_curveTimeInputField.text, out v))
                {
                    v = Mathf.Clamp(v, 0.001f, 0.999f);
                    if (!curve.keys.Any(k => k.time == v))
                    {
                        curveKey.time = v;
                        curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                        _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                        SaveKeyframeCurve();
                    }
                }
            }
            UpdateCurvePointTime();
            UpdateCurve();
        }

        private void UpdateCurvePointTime(float f)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex >= 1 && _selectedKeyframeCurvePointIndex < curve.length - 1)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                float v = Mathf.Clamp(_curveTimeSlider.value, 0.001f, 0.999f);
                if (curve.keys.Any(k => k.time == v) == false)
                {
                    curveKey.time = v;
                    curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                    _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                    SaveKeyframeCurve();
                }
            }
            UpdateCurvePointTime();
            UpdateCurve();
        }

        private void UpdateCurvePointTime()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            UnityEngine.Keyframe curveKey;
            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
                curveKey = curve[_selectedKeyframeCurvePointIndex];
            else
                curveKey = new UnityEngine.Keyframe();
            _curveTimeInputField.text = curveKey.time.ToString("0.00000");
            _curveTimeSlider.SetValueNoCallback(curveKey.time);
        }

        private void UpdateCurvePointValue(string s)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex >= 1 && _selectedKeyframeCurvePointIndex < curve.length - 1)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                float v;
                if (float.TryParse(_curveValueInputField.text, out v))
                {
                    curveKey.value = v;
                    curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                    _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                    SaveKeyframeCurve();
                }
            }
            UpdateCurvePointValue();
            UpdateCurve();
        }

        private void UpdateCurvePointValue(float f)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex >= 1 && _selectedKeyframeCurvePointIndex < curve.length - 1)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                curveKey.value = _curveValueSlider.value;
                curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                SaveKeyframeCurve();
            }
            UpdateCurvePointValue();
            UpdateCurve();
        }

        private void UpdateCurvePointValue()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            UnityEngine.Keyframe curveKey;
            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
                curveKey = curve[_selectedKeyframeCurvePointIndex];
            else
                curveKey = new UnityEngine.Keyframe();
            _curveValueInputField.text = curveKey.value.ToString("0.00000");
            _curveValueSlider.SetValueNoCallback(curveKey.value);
        }

        private void UpdateCurvePointInTangent(string s)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                float v;
                if (float.TryParse(_curveInTangentInputField.text, out v))
                {
                    if (v == 90f || v == -90f)
                        curveKey.inTangent = float.NegativeInfinity;
                    else
                        curveKey.inTangent = Mathf.Tan(v * Mathf.Deg2Rad);
                    curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                    _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                    SaveKeyframeCurve();
                }
            }
            UpdateCurvePointInTangent();
            UpdateCurve();
        }

        private void UpdateCurvePointInTangent(float f)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                if (_curveInTangentSlider.value == 90f || _curveInTangentSlider.value == -90f)
                    curveKey.inTangent = float.PositiveInfinity;
                else
                    curveKey.inTangent = Mathf.Tan(_curveInTangentSlider.value * Mathf.Deg2Rad);
                curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                SaveKeyframeCurve();
            }
            UpdateCurvePointInTangent();
            UpdateCurve();
        }

        private void UpdateCurvePointInTangent()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            UnityEngine.Keyframe curveKey;
            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
                curveKey = curve[_selectedKeyframeCurvePointIndex];
            else
                curveKey = new UnityEngine.Keyframe();
            float v = Mathf.Atan(curveKey.inTangent) * Mathf.Rad2Deg;
            _curveInTangentInputField.text = v.ToString("0.000");
            _curveInTangentSlider.SetValueNoCallback(v);
        }

        private void UpdateCurvePointOutTangent(string s)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                float v;
                if (float.TryParse(_curveOutTangentInputField.text, out v))
                {
                    if (v == 90f || v == -90f)
                        curveKey.outTangent = float.NegativeInfinity;
                    else
                        curveKey.outTangent = Mathf.Tan(v * Mathf.Deg2Rad);
                    curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                    _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                    SaveKeyframeCurve();
                }
            }
            UpdateCurvePointOutTangent();
            UpdateCurve();
        }

        private void UpdateCurvePointOutTangent(float f)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
            {
                UnityEngine.Keyframe curveKey = curve[_selectedKeyframeCurvePointIndex];
                if (_curveOutTangentSlider.value == 90f || _curveOutTangentSlider.value == -90f)
                    curveKey.outTangent = float.NegativeInfinity;
                else
                    curveKey.outTangent = Mathf.Tan(_curveOutTangentSlider.value * Mathf.Deg2Rad);
                curve.RemoveKey(_selectedKeyframeCurvePointIndex);
                _selectedKeyframeCurvePointIndex = curve.AddKey(curveKey);
                SaveKeyframeCurve();
            }
            UpdateCurvePointOutTangent();
            UpdateCurve();
        }

        private void UpdateCurvePointOutTangent()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            UnityEngine.Keyframe curveKey;
            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            if (_selectedKeyframeCurvePointIndex != -1 && _selectedKeyframeCurvePointIndex < curve.length)
                curveKey = curve[_selectedKeyframeCurvePointIndex];
            else
                curveKey = new UnityEngine.Keyframe();
            float v = Mathf.Atan(curveKey.outTangent) * Mathf.Rad2Deg;
            _curveOutTangentInputField.text = v.ToString("0.000");
            _curveOutTangentSlider.SetValueNoCallback(v);
        }

        private void CopyKeyframeCurve()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            _copiedKeyframeCurve.keys = _selectedKeyframes[0].Value.curve.keys;
        }

        private void PasteKeyframeCurve()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            _selectedKeyframes[0].Value.curve.keys = _copiedKeyframeCurve.keys;
            SaveKeyframeCurve();
            UpdateCurve();
        }

        private void InvertKeyframeCurve()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            UnityEngine.Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                UnityEngine.Keyframe key = keys[i];
                key.time = 1 - key.time;
                key.value = 1 - key.value;
                float tmp = key.inTangent;
                key.inTangent = key.outTangent;
                key.outTangent = tmp;
                keys[i] = key;
            }

            Array.Reverse(keys);
            curve.keys = keys;
            SaveKeyframeCurve();
            UpdateCurve();
        }

        private void ApplyKeyframeCurvePreset(AnimationCurve preset)
        {
            if (_selectedKeyframes.Count == 0)
                return;

            _selectedKeyframes[0].Value.curve = new AnimationCurve(preset.keys);
            SaveKeyframeCurve();
            UpdateCurve();
        }

        private void UpdateCursor2()
        {
            if (!_keyframeWindow.activeSelf)
                return;
            if (_selectedKeyframes.Count == 1)
            {
                KeyValuePair<float, Keyframe> selectedKeyframe = _selectedKeyframes[0];

                if (_playbackTime >= selectedKeyframe.Key)
                {
                    KeyValuePair<float, Keyframe> after = selectedKeyframe.Value.parent.keyframes.FirstOrDefault(k => k.Key > selectedKeyframe.Key);
                    if (after.Value != null && _playbackTime <= after.Key)
                    {
                        _cursor2.gameObject.SetActive(true);

                        float normalizedTime = (_playbackTime - selectedKeyframe.Key) / (after.Key - selectedKeyframe.Key);
                        _cursor2.anchoredPosition = new Vector2(normalizedTime * _curveContainer.rectTransform.rect.width, _cursor2.anchoredPosition.y);
                    }
                    else
                        _cursor2.gameObject.SetActive(false);
                }
                else
                    _cursor2.gameObject.SetActive(false);
            }
            else
                _cursor2.gameObject.SetActive(false);
        }

        private void DeleteSelectedKeyframes()
        {
            UIUtility.DisplayConfirmationDialog(result =>
                {
                    if (result)
                    {
#if FEATURE_UNDO
                        UndoPushAction();
#endif
                        DeleteKeyframes(_selectedKeyframes);
                    }
                }, _selectedKeyframes.Count == 1 ? "Are you sure you want to delete this Keyframe?" : "Are you sure you want to delete these Keyframes?"
            );
        }

        private void DeleteKeyframes(params KeyValuePair<float, Keyframe>[] keyframes)
        {
            DeleteKeyframes((IEnumerable<KeyValuePair<float, Keyframe>>)keyframes);
        }

        private void DeleteKeyframes(IEnumerable<KeyValuePair<float, Keyframe>> keyframes, bool removeInterpolables = true)
        {
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            keyframes = keyframes.ToList();
            foreach (KeyValuePair<float, Keyframe> pair in keyframes)
            {
                if (pair.Value == null) //Just a safeguard.
                    continue;
                if (pair.Key < min)
                    min = pair.Key;
                if (pair.Key > max)
                    max = pair.Key;
                try
                {
                    pair.Value.parent.keyframes.Remove(pair.Key);

#if FEATURE_SOUND
                    if (pair.Value.parent.instantAction) {
                        AudioSource audioSource = pair.Value.parent.oci.guideObject.gameObject.GetComponent<AudioSource>();

                        if (audioSource != null) {
                            audioSource.Stop();
                            UnityEngine.Object.Destroy(audioSource);
                        }
                    }
#endif

                    if (removeInterpolables && pair.Value.parent.keyframes.Count == 0)
                        RemoveInterpolable(pair.Value.parent);
                }
                catch (Exception e)
                {
                    Logger.LogError("Couldn't delete keyframe with time \"" + pair.Key + "\" and value \"" + pair.Value + "\" from interpolable \"" + pair.Value.parent + "\"\n" + e);
                }
            }

            if (Input.GetKey(KeyCode.LeftAlt))
            {
                double duration = max - min + (_blockLength / _divisions);
                //IDK, grouping didn't work so I'm doing it like this
                HashSet<Interpolable> processedParents = new HashSet<Interpolable>();
                foreach (KeyValuePair<float, Keyframe> k in keyframes)
                {
                    if (processedParents.Contains(k.Value.parent) != false)
                        continue;
                    processedParents.Add(k.Value.parent);
                    foreach (KeyValuePair<float, Keyframe> pair in k.Value.parent.keyframes.ToList())
                        if (pair.Key > min)
                            MoveKeyframe(pair.Value, (float)(pair.Key - duration));
                }
            }
            _selectedKeyframes.RemoveAll(elem => elem.Value == null || keyframes.Any(k => k.Value == elem.Value));

            UpdateGrid();
            UpdateKeyframeWindow(false);
        }

#if FIXED_096_DEBUG
        private void SetObjectCtrlDirty(ObjectCtrlInfo objectCtrlInfo){
            int hashcode = 0;

            if (objectCtrlInfo != null)
                hashcode = objectCtrlInfo.GetHashCode();

            ObjectCtrlItem objectCtrlItem = null;
            if (_ociControlMgmt.TryGetValue(hashcode, out objectCtrlItem)) {
                objectCtrlItem.dirty = true;
            }
        }
#endif

        private void SaveKeyframeTime(float time)
        {
            for (int i = 0; i < _selectedKeyframes.Count; i++)
            {
                KeyValuePair<float, Keyframe> pair = _selectedKeyframes[i];
                Keyframe potentialDuplicateKeyframe;
                if (pair.Value.parent.keyframes.TryGetValue(time, out potentialDuplicateKeyframe) && potentialDuplicateKeyframe != pair.Value)
                    continue;
                pair.Value.parent.keyframes.Remove(pair.Key);
                pair.Value.parent.keyframes.Add(time, pair.Value);
                _selectedKeyframes[i] = new KeyValuePair<float, Keyframe>(time, pair.Value);
            }

            UpdateKeyframeTimeTextField();
            this.ExecuteDelayed2(UpdateCursor2);
            UpdateGrid();
        }

        private void SaveKeyframeCurve()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve modifiedCurve = _selectedKeyframes[0].Value.curve;
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                pair.Value.curve = new AnimationCurve(modifiedCurve.keys);
        }

        private void UpdateKeyframeWindow(bool changeShowState = true)
        {
            if (_selectedKeyframes.Count == 0)
            {
                CloseKeyframeWindow();
                return;
            }
            if (changeShowState)
                OpenKeyframeWindow();

            IEnumerable<IGrouping<Interpolable, KeyValuePair<float, Keyframe>>> interpolableGroups = _selectedKeyframes.GroupBy(e => e.Value.parent);
            bool singleInterpolable = interpolableGroups.Count() == 1;
            Interpolable first = interpolableGroups.First().Key;
            _keyframeInterpolableNameText.text = singleInterpolable ? (string.IsNullOrEmpty(first.alias) ? first.name : first.alias) : "Multiple selected";
            _keyframeSelectPrevButton.interactable = _selectedKeyframes.Count == 1;
            _keyframeSelectNextButton.interactable = _selectedKeyframes.Count == 1;
            _keyframeTimeTextField.interactable = interpolableGroups.All(g => g.Count() == 1);
            _keyframeUseCurrentTimeButton.interactable = _keyframeTimeTextField.interactable;
            _keyframeDeleteButtonText.text = _selectedKeyframes.Count == 1 ? "Delete" : "Delete all";

            UpdateKeyframeTimeTextField();
            UpdateKeyframeValueText();
            this.ExecuteDelayed2(UpdateCurve);
            this.ExecuteDelayed2(UpdateCursor2);
        }

        private void UpdateKeyframeTimeTextField()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            float t = _selectedKeyframes[0].Key;
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
            {
                if (t != pair.Key)
                {
                    _keyframeTimeTextField.text = "Multiple times";
                    return;
                }
            }
            _keyframeTimeTextField.text = $"{Mathf.FloorToInt(t / 60):00}:{t % 60:00.########}";
        }

        private void UpdateKeyframeValueText()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            object v = _selectedKeyframes[0].Value.value;
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
            {
                if (v.Equals(pair.Value.value) == false)
                {
                    _keyframeValueText.text = "Multiple values";
                    return;
                }
            }

            _keyframeValueText.text = v != null ? v.ToString() : "null";

        }

        private void UpdateCurve()
        {
            if (_selectedKeyframes.Count == 0)
                return;

            AnimationCurve curve = _selectedKeyframes[0].Value.curve;
            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
            {
                if (CompareCurves(curve, pair.Value.curve) == false)
                {
                    curve = null;
                    break;
                }
            }
            int length = 0;
            if (curve != null)
            {
                length = curve.length;
                for (int i = 0; i < _curveTexture.width; i++)
                {
                    float v = curve.Evaluate(i / (_curveTexture.width - 1f));
                    _curveTexture.SetPixel(i, 0, new Color(v, v, v, v));
                }
            }
            else
            {
                for (int i = 0; i < _curveTexture.width; i++)
                    _curveTexture.SetPixel(i, 0, new Color(2f, 2f, 2f, 2f));
            }

            _curveTexture.Apply(false);
            _curveContainer.material.mainTexture = _curveTexture;
            _curveContainer.enabled = false;
            _curveContainer.enabled = true;

            int displayIndex = 0;
            for (int i = 0; i < length; ++i)
            {
                UnityEngine.Keyframe curveKeyframe = curve[i];
                CurveKeyframeDisplay display;
                if (displayIndex < _displayedCurveKeyframes.Count)
                    display = _displayedCurveKeyframes[displayIndex];
                else
                {
                    display = new CurveKeyframeDisplay();
                    display.gameObject = GameObject.Instantiate(_curveKeyframePrefab);
                    display.gameObject.hideFlags = HideFlags.None;
#if FIXED_096
                    display.image = GetComponentInChildren<RawImage>(); // 성능 올리기
#else
                    display.image = display.gameObject.transform.Find("RawImage").GetComponent<RawImage>();
#endif
                    display.gameObject.transform.SetParent(_curveContainer.transform);
                    display.gameObject.transform.localScale = Vector3.one;
                    display.gameObject.transform.localPosition = Vector3.zero;

                    display.pointerDownHandler = display.gameObject.AddComponent<PointerDownHandler>();
                    display.scrollHandler = display.gameObject.AddComponent<ScrollHandler>();
                    display.dragHandler = display.gameObject.AddComponent<DragHandler>();
                    display.pointerEnterHandler = display.gameObject.AddComponent<PointerEnterHandler>();

                    _displayedCurveKeyframes.Add(display);
                }

                int i1 = i;
                display.pointerDownHandler.onPointerDown = (e) =>
                {
                    if (e.button == PointerEventData.InputButton.Left)
                    {
                        _selectedKeyframeCurvePointIndex = i1;
                        UpdateCurve();
                    }
                    if (i1 == 0 || i1 == curve.length - 1)
                        return;
                    if (e.button == PointerEventData.InputButton.Middle && Input.GetKey(KeyCode.LeftControl))
                    {
                        foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                            pair.Value.curve.RemoveKey(i1);
                        UpdateCurve();
                    }
                };
                display.scrollHandler.onScroll = (e) =>
                {
                    UnityEngine.Keyframe k = curve[i1];
                    float offset = e.scrollDelta.y > 0 ? Mathf.PI / 180f : -Mathf.PI / 180f;
                    foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                        pair.Value.curve.RemoveKey(i1);
                    if (Input.GetKey(KeyCode.LeftControl))
                        k.inTangent = Mathf.Tan(Mathf.Atan(k.inTangent) + offset);
                    else if (Input.GetKey(KeyCode.LeftAlt))
                        k.outTangent = Mathf.Tan(Mathf.Atan(k.outTangent) + offset);
                    else
                    {
                        k.inTangent = Mathf.Tan(Mathf.Atan(k.inTangent) + offset);
                        k.outTangent = Mathf.Tan(Mathf.Atan(k.outTangent) + offset);
                    }
                    foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                        pair.Value.curve.AddKey(k);
                    UpdateCurve();
                };
                display.dragHandler.onDrag = (e) =>
                {
                    if (i1 == 0 || i1 == curve.length - 1)
                        return;
                    Vector2 localPoint;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveContainer.rectTransform, e.position, e.pressEventCamera, out localPoint))
                    {
                        localPoint.x = Mathf.Clamp(localPoint.x, 0f, _curveContainer.rectTransform.rect.width);
                        localPoint.y = Mathf.Clamp(localPoint.y, 0f, _curveContainer.rectTransform.rect.height);
                        if (Input.GetKey(KeyCode.LeftShift))
                        {
                            Vector2 curveGridCellSize = new Vector2(_curveContainer.rectTransform.rect.width * _curveGridCellSizePercent, _curveContainer.rectTransform.rect.height * _curveGridCellSizePercent);
                            float mod = localPoint.x % curveGridCellSize.x;
                            if (mod / curveGridCellSize.x > 0.5f)
                                localPoint.x += curveGridCellSize.x - mod;
                            else
                                localPoint.x -= mod;
                            mod = localPoint.y % curveGridCellSize.y;
                            if (mod / curveGridCellSize.y > 0.5f)
                                localPoint.y += curveGridCellSize.y - mod;
                            else
                                localPoint.y -= mod;
                        }
                        ((RectTransform)display.gameObject.transform).anchoredPosition = localPoint;
                    }
                };
                display.dragHandler.onEndDrag = (e) =>
                {
                    if (i1 == 0 || i1 == curve.length - 1)
                        return;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveContainer.rectTransform, e.position, e.pressEventCamera, out Vector2 localPoint))
                    {

                        float time = localPoint.x / _curveContainer.rectTransform.rect.width;
                        float value = localPoint.y / _curveContainer.rectTransform.rect.height;
                        if (Input.GetKey(KeyCode.LeftShift))
                        {
                            float mod = time % _curveGridCellSizePercent;
                            if (mod / _curveGridCellSizePercent > 0.5f)
                                time += _curveGridCellSizePercent - mod;
                            else
                                time -= mod;
                            mod = value % _curveGridCellSizePercent;
                            if (mod / _curveGridCellSizePercent > 0.5f)
                                value += _curveGridCellSizePercent - mod;
                            else
                                value -= mod;
                        }
                        if (time > 0 && time < 1 && value >= 0 && value <= 1 && curve.keys.Any(k => k.time == time) == false)
                        {
                            UnityEngine.Keyframe curveKey = curve[i1];
                            curveKey.time = time;
                            curveKey.value = value;
                            foreach (KeyValuePair<float, Keyframe> pair in _selectedKeyframes)
                            {
                                pair.Value.curve.RemoveKey(i1);
                                pair.Value.curve.AddKey(curveKey);
                            }
                        }
                        UpdateCurve();
                    }
                };
                display.pointerEnterHandler.onPointerEnter = (e) =>
                {
                    _tooltip.transform.parent.gameObject.SetActive(true);
                    UnityEngine.Keyframe k = curve[i1];
                    _tooltip.text = $"T: {k.time:0.000}, V: {k.value:0.###}\nIn: {Mathf.Atan(k.inTangent) * Mathf.Rad2Deg:0.#}, Out:{Mathf.Atan(k.outTangent) * Mathf.Rad2Deg:0.#}";
                };
                display.pointerEnterHandler.onPointerExit = (e) => { _tooltip.transform.parent.gameObject.SetActive(false); };

                display.image.color = i == _selectedKeyframeCurvePointIndex ? Color.green : (Color)new Color32(44, 153, 160, 255);
                display.gameObject.SetActive(true);
                ((RectTransform)display.gameObject.transform).anchoredPosition = new Vector2(curveKeyframe.time * _curveContainer.rectTransform.rect.width, curveKeyframe.value * _curveContainer.rectTransform.rect.height);
                ++displayIndex;
            }
            for (; displayIndex < _displayedCurveKeyframes.Count; ++displayIndex)
                _displayedCurveKeyframes[displayIndex].gameObject.SetActive(false);

            UpdateCurvePointTime();
            UpdateCurvePointValue();
            UpdateCurvePointInTangent();
            UpdateCurvePointOutTangent();
        }

        private bool CompareCurves(AnimationCurve x, AnimationCurve y)
        {
            if (x.length != y.length)
                return false;
            for (int i = 0; i < x.length; i++)
            {
                UnityEngine.Keyframe keyX = x.keys[i];
                UnityEngine.Keyframe keyY = y.keys[i];
                if (keyX.time != keyY.time ||
                    keyX.value != keyY.value ||
                    keyX.inTangent != keyY.inTangent ||
                    keyX.outTangent != keyY.outTangent)
                    return false;
            }
            return true;
        }

        #endregion

        #endregion

        #region Saves

#if KOIKATSU || AISHOUJO || HONEYSELECT2

#if FEATURE_AUTOGEN
        private void SceneInit() {
#if FIXED_096 
            CancelInvoke(nameof(_self.DelayCursorUpdate));
#endif            
            Stop(); 
            isAutoGenerating = false;

            _selectedKeyframes.Clear();
            _interpolablesTree.Clear();            
            _interpolables.Clear();            
            _selectedOCI = null;
#if FEATURE_UNDO
            _undoStack.Clear();
            _redoStack.Clear();
#endif
#if FEATURE_SOUND
            UnregisterSoundTimers();
            DeleteTemporaryCache();
            _instantActionInterpolables.Clear();
#endif
#if FIXED_096            
            foreach (KeyValuePair<int, ObjectCtrlItem> pair in _ociControlMgmt)
            {
                int key = pair.Key;
                ObjectCtrlItem ociItem = pair.Value;

                foreach (KeyframeDisplay displayItem in ociItem.displayedKeyframes)
                    ReturnToKeyframeDisplayPool(displayItem);
                
                ociItem.displayedKeyframes.Clear();
                GameObject.DestroyImmediate(ociItem.keyframeGroup);
                ociItem = null;
            }

            // 0번을 제외한 나머지는 모두 제거
            var value = _ociControlMgmt[0];
            _ociControlMgmt.Clear();
            _ociControlMgmt[0] = value; 
#endif

            UpdateGrid();
        }
#endif
        private void OnSceneLoad(string path)
        {
#if FEATURE_AUTOGEN
            SceneInit();
#endif
#if FEATURE_SOUND
            _uuid = Guid.NewGuid();
            UnityEngine.Debug.Log($"timeline _uuid {_uuid}");
            CreateSoundToTemporaryCache();
#endif
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            if (node == null)
                return;
            SceneLoad(path, node);
        }

        private void OnSceneImport(string path)
        {
            PluginData data = ExtendedSave.GetSceneExtendedDataById(_extSaveKey);
            if (data == null)
                return;
            XmlDocument doc = new XmlDocument();
            doc.LoadXml((string)data.data["sceneInfo"]);
            XmlNode node = doc.FirstChild;
            if (node == null)
                return;
            SceneImport(path, node);
#if FEATURE_SOUND
            PluginData data1 = ExtendedSave.GetSceneExtendedDataById(_extSaveKey2);
            if (data1 == null)
                return;
            XmlDocument doc1 = new XmlDocument();
            doc.LoadXml((string)data1.data["sceneSoundInfo"]);
            XmlNode node1 = doc1.FirstChild;
            if (node1 == null)
                return;
            // create sound
#endif
        }

        private void OnSceneSave(string path)
        {
            using (StringWriter stringWriter = new StringWriter())
            using (XmlTextWriter xmlWriter = new XmlTextWriter(stringWriter))
            {
                xmlWriter.WriteStartElement("root");
                SceneWrite(path, xmlWriter);
                xmlWriter.WriteEndElement();

                PluginData data = new PluginData();
                data.version = Timeline._saveVersion;
                data.data.Add("sceneInfo", stringWriter.ToString());
                ExtendedSave.SetSceneExtendedDataById(_extSaveKey, data);
            }
#if FEATURE_SOUND
            using (StringWriter stringWriter1 = new StringWriter())
            using (XmlTextWriter xmlWriter1 = new XmlTextWriter(stringWriter1))
            {
                xmlWriter1.WriteStartElement("root");
                SceneSoundWrite(xmlWriter1);
                xmlWriter1.WriteEndElement();

                PluginData data1 = new PluginData();
                data1.version = Timeline._saveVersion;
                data1.data.Add("sceneSoundInfo", stringWriter1.ToString());
                ExtendedSave.SetSceneExtendedDataById(_extSaveKey2, data1);
                DeleteTemporaryCache();
            }
#endif
        }
#endif

        private void SceneLoad(string path, XmlNode node)
        {
            if (node == null)
                return;
            this.ExecuteDelayed2(() =>
            {
                // _interpolables.Clear();
                // _interpolablesTree.Clear();
                // _selectedOCI = null;
                // _selectedKeyframes.Clear();

                List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();
                SceneLoad(node, dic);

                UpdateInterpolablesView();
                CloseKeyframeWindow();
            }, 20);
        }

        private void SceneImport(string path, XmlNode node)
        {
            Dictionary<int, ObjectCtrlInfo> toIgnore = new Dictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl);
            this.ExecuteDelayed2(() =>
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = Studio.Studio.Instance.dicObjectCtrl.Where(e => toIgnore.ContainsKey(e.Key) == false).OrderBy(e => SceneInfo_Import_Patches._newToOldKeys[e.Key]).ToList();
                SceneLoad(node, dic);

                UpdateInterpolablesView();
            }, 20);
        }

        private void SceneWrite(string path, XmlTextWriter writer)
        {
            List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();
            writer.WriteAttributeString("duration", XmlConvert.ToString(_duration));
            writer.WriteAttributeString("blockLength", XmlConvert.ToString(_blockLength));
            writer.WriteAttributeString("divisions", XmlConvert.ToString(_divisions));
            writer.WriteAttributeString("timeScale", XmlConvert.ToString(Time.timeScale));
            foreach (INode node in _interpolablesTree.tree)
                WriteInterpolableTree(node, writer, dic);
        }

#if FEATURE_SOUND
        private void SceneSoundWrite(XmlTextWriter writer)
        {
            Dictionary<string, SoundItem> activeSoundFiles = new Dictionary<string, SoundItem>();
            activeSoundFiles = SearchActiveSound();
            foreach (KeyValuePair<string, SoundItem> activeSoundFile in activeSoundFiles) {
                writer.WriteStartElement("file");
                writer.WriteAttributeString("name", activeSoundFile.Key);

                string sceneFilePath = Path.Combine(Application.temporaryCachePath, activeSoundFile.Key);
                writer.WriteAttributeString("data", Convert.ToBase64String(File.ReadAllBytes(sceneFilePath)));
                writer.WriteEndElement();
            }
        }
#endif
        private void SceneLoad(XmlNode node, List<KeyValuePair<int, ObjectCtrlInfo>> dic)
        {
            ReadInterpolableTree(node, dic);

            if (node.Attributes["duration"] != null)
                _duration = XmlConvert.ToSingle(node.Attributes["duration"].Value);
            else
            {
                _duration = 0f;
                foreach (KeyValuePair<int, Interpolable> pair in _interpolables)
                {
                    KeyValuePair<float, Keyframe> last = pair.Value.keyframes.LastOrDefault();
                    if (_duration < last.Key)
                        _duration = last.Key;
                }
                if (Mathf.Approximately(_duration, 0f))
                    _duration = 10f;
            }
            _blockLength = node.Attributes["blockLength"] != null ? XmlConvert.ToSingle(node.Attributes["blockLength"].Value) : 10f;
            _divisions = node.Attributes["divisions"] != null ? XmlConvert.ToInt32(node.Attributes["divisions"].Value) : 10;
            Time.timeScale = node.Attributes["timeScale"] != null ? XmlConvert.ToSingle(node.Attributes["timeScale"].Value) : 1f;
            _blockLengthInputField.text = _blockLength.ToString();
            _divisionsInputField.text = _divisions.ToString();
            _speedInputField.text = Time.timeScale.ToString("0.#####");

            if (ConfigAutoplay.Value == Autoplay.Yes)
            {
                Stop();
                Play();
            }
            else if (ConfigAutoplay.Value == Autoplay.No)
            {
                Stop();
            }
        }

        private void LoadSingle(string path)
        {
            List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();
            XmlDocument document = new XmlDocument();
            try
            {
                document.Load(path);
                ReadInterpolableTree(document.FirstChild, dic, _selectedOCI);
#if KOIKATSU || SUNSHINE
                string docGUID = document.FirstChild.Attributes?["GUID"]?.InnerText;
                int docGr = document.FirstChild.ReadInt("animationGroup");
                int docCa = document.FirstChild.ReadInt("animationCategory");
                int docNo = document.FirstChild.ReadInt("animationNo");
                OCIChar character = _selectedOCI as OCIChar;
                StudioResolveInfo resolveInfo = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.Slot == docNo && x.GUID == docGUID && x.Group == docGr && x.Category == docCa);
                if (character != null)
                {
                    character.LoadAnime(docGr, docCa, resolveInfo != null ? resolveInfo.LocalSlot : docNo);
                }
#else           //AI&HS2 Studio use original ID(management number) for animation zipmods by default
                OCIChar character = _selectedOCI as OCIChar;
                if (character != null)
                {
                    character.LoadAnime(document.FirstChild.ReadInt("animationGroup"),
                            document.FirstChild.ReadInt("animationCategory"),
                            document.FirstChild.ReadInt("animationNo"));
                }
#endif
            }
            catch (Exception e)
            {
                Logger.LogError("Could not load data for OCI.\n" + document.FirstChild + "\n" + e);
            }
            UpdateInterpolablesView();
        }

        private void SaveXmlSingle(string path)
        {
            using (XmlTextWriter writer = new XmlTextWriter(path, Encoding.UTF8))
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();
                writer.WriteStartElement("root");

                OCIChar character = _selectedOCI as OCIChar;

                if (character != null)
                {
#if KOIKATSU || SUNSHINE
                    OICharInfo.AnimeInfo info = character.oiCharInfo.animeInfo;
                    StudioResolveInfo resolveInfo = UniversalAutoResolver.LoadedStudioResolutionInfo.FirstOrDefault(x => x.LocalSlot == info.no && x.Group == info.group && x.Category == info.category);
                    writer.WriteAttributeString("GUID", info.no >= UniversalAutoResolver.BaseSlotID && resolveInfo != null ? resolveInfo.GUID : "");
                    writer.WriteValue("animationGroup", info.group);
                    writer.WriteValue("animationCategory", info.category);
                    writer.WriteValue("animationNo", info.no >= UniversalAutoResolver.BaseSlotID && resolveInfo != null ? resolveInfo.Slot : info.no);
#else           //AI&HS2 Studio use original ID(management number) for animation zipmods by default
                    OICharInfo.AnimeInfo info = character.oiCharInfo.animeInfo;
                    writer.WriteValue("animationCategory", info.category);
                    writer.WriteValue("animationGroup", info.group);
                    writer.WriteValue("animationNo", info.no);
#endif
                }

                foreach (INode node in _interpolablesTree.tree)
                    WriteInterpolableTree(node, writer, dic, leafNode => leafNode.obj.oci == _selectedOCI);
                writer.WriteEndElement();
            }
        }

        private void ReadInterpolableTree(XmlNode groupNode, List<KeyValuePair<int, ObjectCtrlInfo>> dic, ObjectCtrlInfo overrideOci = null, GroupNode<InterpolableGroup> group = null)
        {
            foreach (XmlNode interpolableNode in groupNode.ChildNodes)
            {
                switch (interpolableNode.Name)
                {
                    case "interpolable":
                        ReadInterpolable(interpolableNode, dic, overrideOci, group);
                        break;
                    case "interpolableGroup":
                        string groupName = interpolableNode.Attributes["name"].Value;
                        GroupNode<InterpolableGroup> newGroup = _interpolablesTree.AddGroup(new InterpolableGroup { name = groupName }, group);
                        ReadInterpolableTree(interpolableNode, dic, overrideOci, newGroup);
                        break;
                }
            }
        }

        private void WriteInterpolableTree(INode interpolableNode, XmlTextWriter writer, List<KeyValuePair<int, ObjectCtrlInfo>> dic, Func<LeafNode<Interpolable>, bool> predicate = null)
        {
            switch (interpolableNode.type)
            {
                case INodeType.Leaf:
                    LeafNode<Interpolable> leafNode = (LeafNode<Interpolable>)interpolableNode;
                    if (predicate == null || predicate(leafNode))
                        WriteInterpolable(leafNode.obj, writer, dic);
                    break;
                case INodeType.Group:
                    GroupNode<InterpolableGroup> group = (GroupNode<InterpolableGroup>)interpolableNode;
                    bool shouldWriteGroup = true;
                    if (predicate != null)
                        shouldWriteGroup = _interpolablesTree.Any(group, predicate);
                    if (shouldWriteGroup)
                    {
                        writer.WriteStartElement("interpolableGroup");
                        writer.WriteAttributeString("name", group.obj.name);

                        foreach (INode child in group.children)
                            WriteInterpolableTree(child, writer, dic, predicate);

                        writer.WriteEndElement();
                    }
                    break;
            }
        }

        private void ReadInterpolable(XmlNode interpolableNode, List<KeyValuePair<int, ObjectCtrlInfo>> dic, ObjectCtrlInfo overrideOci = null, GroupNode<InterpolableGroup> group = null)
        {
            bool added = false;
            Interpolable interpolable = null;
            try
            {
                if (interpolableNode.Name == "interpolable")
                {
                    string ownerId = interpolableNode.Attributes["owner"].Value;
                    ObjectCtrlInfo oci = null;
                    if (overrideOci != null)
                        oci = overrideOci;
                    else if (interpolableNode.Attributes["objectIndex"] != null)
                    {
                        int objectIndex = XmlConvert.ToInt32(interpolableNode.Attributes["objectIndex"].Value);
                        if (objectIndex >= dic.Count)
                            return;
                        oci = dic[objectIndex].Value;
                    }

                    string id = interpolableNode.Attributes["id"].Value;
                    InterpolableModel model = _interpolableModelsList.Find(i => i.owner == ownerId && i.id == id);
                    if (model == null /*|| model.isCompatibleWithTarget(oci) == false*/) //todo Might need to get this back on in the future, depending on how things end up going; add logging for discarded entries?
                        return;
                    if (model.readParameterFromXml != null)
                        interpolable = new Interpolable(oci, model.readParameterFromXml(oci, interpolableNode), model);
                    else
                        interpolable = new Interpolable(oci, model);

#if FEATURE_SOUND
                    if (model.id == "SoundSFXControl") {
                        interpolable.instantAction = true;
                        _instantActionInterpolables.Add(interpolable.oci.GetHashCode(), interpolable);
                    } else if (model.id == "SoundBGControl") {
                        interpolable.instantAction = true;
                        _instantActionInterpolables.Add(interpolable.oci.GetHashCode(), interpolable);
                    } else {
                        interpolable.instantAction = false;
                    }
#endif

                    interpolable.enabled = interpolableNode.Attributes["enabled"] == null || XmlConvert.ToBoolean(interpolableNode.Attributes["enabled"].Value);

                    if (interpolableNode.Attributes["bgColorR"] != null)
                    {
                        interpolable.color = new Color(
                                XmlConvert.ToSingle(interpolableNode.Attributes["bgColorR"].Value),
                                XmlConvert.ToSingle(interpolableNode.Attributes["bgColorG"].Value),
                                XmlConvert.ToSingle(interpolableNode.Attributes["bgColorB"].Value)
                        );
                    }

                    if (interpolableNode.Attributes["alias"] != null)
                        interpolable.alias = interpolableNode.Attributes["alias"].Value;

                    if (_interpolables.ContainsKey(interpolable.GetHashCode()) == false)
                    {
                        _interpolables.Add(interpolable.GetHashCode(), interpolable);
                        _interpolablesTree.AddLeaf(interpolable, group);
                        added = true;
                        foreach (XmlNode keyframeNode in interpolableNode.ChildNodes)
                        {
                            if (keyframeNode.Name == "keyframe")
                            {
                                float time = XmlConvert.ToSingle(keyframeNode.Attributes["time"].Value);

                                object value = interpolable.ReadValueFromXml(keyframeNode);
                                List<UnityEngine.Keyframe> curveKeys = new List<UnityEngine.Keyframe>();
                                foreach (XmlNode curveKeyNode in keyframeNode.ChildNodes)
                                {
                                    if (curveKeyNode.Name == "curveKeyframe")
                                    {
                                        UnityEngine.Keyframe curveKey = new UnityEngine.Keyframe(
                                                XmlConvert.ToSingle(curveKeyNode.Attributes["time"].Value),
                                                XmlConvert.ToSingle(curveKeyNode.Attributes["value"].Value),
                                                XmlConvert.ToSingle(curveKeyNode.Attributes["inTangent"].Value),
                                                XmlConvert.ToSingle(curveKeyNode.Attributes["outTangent"].Value));
                                        curveKeys.Add(curveKey);
                                    }
                                }

                                AnimationCurve curve;
                                if (curveKeys.Count == 0)
                                    curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
                                else
                                    curve = new AnimationCurve(curveKeys.ToArray());

                                Keyframe keyframe = new Keyframe(value, interpolable, curve);
                                interpolable.keyframes.Add(time, keyframe);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogError("Couldn't load interpolable with the following XML:\n" + interpolableNode.OuterXml + "\n" + e);
                if (added)
                    RemoveInterpolable(interpolable);
            }
        }

        private void WriteInterpolable(Interpolable interpolable, XmlTextWriter writer, List<KeyValuePair<int, ObjectCtrlInfo>> dic)
        {
            if (interpolable.keyframes.Count == 0)
                return;
            using (StringWriter stream = new StringWriter())
            {
                using (XmlTextWriter localWriter = new XmlTextWriter(stream))
                {
                    try
                    {
                        int objectIndex = -1;
                        if (interpolable.oci != null)
                        {
                            objectIndex = dic.FindIndex(e => e.Value == interpolable.oci);
                            if (objectIndex == -1)
                                return;
                        }

                        localWriter.WriteStartElement("interpolable");
                        localWriter.WriteAttributeString("enabled", XmlConvert.ToString(interpolable.enabled));
                        localWriter.WriteAttributeString("owner", interpolable.owner);
                        if (objectIndex != -1)
                            localWriter.WriteAttributeString("objectIndex", XmlConvert.ToString(objectIndex));
                        localWriter.WriteAttributeString("id", interpolable.id);

                        if (interpolable.writeParameterToXml != null)
                            interpolable.writeParameterToXml(interpolable.oci, localWriter, interpolable.parameter);
                        localWriter.WriteAttributeString("bgColorR", XmlConvert.ToString(interpolable.color.r));
                        localWriter.WriteAttributeString("bgColorG", XmlConvert.ToString(interpolable.color.g));
                        localWriter.WriteAttributeString("bgColorB", XmlConvert.ToString(interpolable.color.b));

                        localWriter.WriteAttributeString("alias", interpolable.alias);

                        foreach (KeyValuePair<float, Keyframe> keyframePair in interpolable.keyframes)
                        {
                            localWriter.WriteStartElement("keyframe");
                            localWriter.WriteAttributeString("time", XmlConvert.ToString(Math.Round(keyframePair.Key, 3)));

                            interpolable.WriteValueToXml(localWriter, keyframePair.Value.value);

                            foreach (UnityEngine.Keyframe curveKey in keyframePair.Value.curve.keys)
                            {
                                localWriter.WriteStartElement("curveKeyframe");
                                localWriter.WriteAttributeString("time", XmlConvert.ToString(Math.Round(curveKey.time, 3)));
                                localWriter.WriteAttributeString("value", XmlConvert.ToString(curveKey.value));
                                localWriter.WriteAttributeString("inTangent", XmlConvert.ToString(curveKey.inTangent));
                                localWriter.WriteAttributeString("outTangent", XmlConvert.ToString(curveKey.outTangent));
                                localWriter.WriteEndElement();
                            }

                            localWriter.WriteEndElement();
                        }

                        localWriter.WriteEndElement();
                    }
                    catch (Exception e)
                    {
                        Logger.LogError("Couldn't save interpolable with the following value:\n" + interpolable + "\n" + e);
                        return;
                    }
                }
                writer.WriteRaw(stream.ToString());
            }
        }

        private void OnDuplicate(ObjectCtrlInfo source, ObjectCtrlInfo destination)
        {
            this.ExecuteDelayed2(() =>
            {
                List<KeyValuePair<int, ObjectCtrlInfo>> dic = new SortedDictionary<int, ObjectCtrlInfo>(Studio.Studio.Instance.dicObjectCtrl).ToList();

                using (StringWriter stream = new StringWriter())
                {
                    using (XmlTextWriter writer = new XmlTextWriter(stream))
                    {
                        writer.WriteStartElement("root");

                        foreach (INode node in _interpolablesTree.tree)
                            WriteInterpolableTree(node, writer, dic, leafNode => leafNode.obj.oci == source);

                        writer.WriteEndElement();
                    }

                    try
                    {
                        XmlDocument document = new XmlDocument();
                        document.LoadXml(stream.ToString());

                        ReadInterpolableTree(document.FirstChild, dic, destination);
                    }
                    catch (Exception e)
                    {
                        Logger.LogError("Could not duplicate data for OCI.\n" + stream + "\n" + e);
                    }

                }
            }, 20);
        }
        #endregion

        #region Patches
#if HONEYSELECT
        [HarmonyPatch(typeof(Expression), "Start")]
#elif KOIKATSU
        [HarmonyPatch(typeof(Expression), "Initialize")]
#endif
        private static class Expression_Start_Patches
        {
            private static void Prefix(Expression __instance)
            {
                _self._allExpressions.Add(__instance);
            }
        }

        [HarmonyPatch(typeof(Expression), "OnDestroy")]
        private static class Expression_OnDestroy_Patches
        {
            private static void Prefix(Expression __instance)
            {
                _self._allExpressions.Remove(__instance);
            }
        }

        [HarmonyPatch(typeof(Expression), "LateUpdate"), HarmonyBefore("com.joan6694.illusionplugins.nodesconstraints")]
        private static class Expression_LateUpdate_Patches
        {
            private static void Postfix()
            {
                _self._currentExpressionIndex++;
                if (_self._currentExpressionIndex == _self._totalActiveExpressions)
                    _self.PostLateUpdate();
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "Duplicate")]
        private class Studio_Duplicate_Patches
        {
            public static void Postfix(Studio.Studio __instance)
            {
                foreach (KeyValuePair<int, int> pair in SceneInfo_Import_Patches._newToOldKeys)
                {
                    ObjectCtrlInfo source;
                    if (__instance.dicObjectCtrl.TryGetValue(pair.Value, out source) == false)
                        continue;
                    ObjectCtrlInfo destination;
                    if (__instance.dicObjectCtrl.TryGetValue(pair.Key, out destination) == false)
                        continue;
                    if (source is OCIChar && destination is OCIChar || source is OCIItem && destination is OCIItem)
                        _self.OnDuplicate(source, destination);
                }
            }
        }

        [HarmonyPatch(typeof(ObjectInfo), "Load", new[] { typeof(BinaryReader), typeof(Version), typeof(bool), typeof(bool) })]
        private static class ObjectInfo_Load_Patches
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                int count = 0;
                List<CodeInstruction> instructionsList = instructions.ToList();
                foreach (CodeInstruction inst in instructionsList)
                {
                    yield return inst;
                    if (count != 2 && inst.ToString().Contains("ReadInt32"))
                    {
                        ++count;
                        if (count == 2)
                        {
                            yield return new CodeInstruction(OpCodes.Ldarg_0);
                            yield return new CodeInstruction(OpCodes.Call, typeof(ObjectInfo_Load_Patches).GetMethod(nameof(Injected), BindingFlags.NonPublic | BindingFlags.Static));
                        }
                    }
                }
            }

            private static int Injected(int originalIndex, ObjectInfo __instance)
            {
                SceneInfo_Import_Patches._newToOldKeys.Add(__instance.dicKey, originalIndex);
                return originalIndex; //Doing this so other transpilers can use this value if they want
            }
        }

        [HarmonyPatch(typeof(SceneInfo), "Import", new[] { typeof(BinaryReader), typeof(Version) })]
        private static class SceneInfo_Import_Patches //This is here because I fucked up the save format making it impossible to import scenes correctly
        {
            internal static readonly Dictionary<int, int> _newToOldKeys = new Dictionary<int, int>();

            private static void Prefix()
            {
                _newToOldKeys.Clear();
            }
        }

        private static void OnGuideClick()
        {
            var manager = GuideObjectManager.Instance;
            GuideObject go = manager.selectObject;

            if (go == null || !Input.GetKey(KeyCode.LeftAlt))
                return;

            var interpolables = _self._interpolables.Where(i => i.Value.parameter is GuideObject g && g == go).Select(pair => pair.Value).ToArray();

            if (interpolables.Length <= 0)
                return;

            int select = 0;

            if (interpolables.Length > 1)
            {
                //If there is a mode selected in the studio, select that interpolation.
                string keyword = null;

                switch (manager.mode)
                {
                    case 0:
                        keyword = "Position";
                        break;

                    case 1:
                        keyword = "Rotation";
                        break;

                    case 2:
                        keyword = "Scale";
                        break;
                }

                if (keyword != null)
                {
                    for (int i = 0; i < interpolables.Length; ++i)
                        if (interpolables[i].name.Contains(keyword))
                        {
                            select = i;
                            break;
                        }
                }
            }

            _self.HighlightInterpolable(interpolables[select]);
        }

        [HarmonyPatch(typeof(GuideSelect), nameof(GuideSelect.OnPointerClick), new[] { typeof(PointerEventData) })]
        private static class GuideSelect_OnPointerClick_Patches
        {
            private static void Postfix() => OnGuideClick();
        }

        [HarmonyPatch(typeof(GuideMove), nameof(GuideMove.OnPointerDown), new[] { typeof(PointerEventData) })]
        private static class GuideMove_OnPointerDown_Patches
        {
            private static void Postfix() => OnGuideClick();
        }

        [HarmonyPatch(typeof(GuideRotation), nameof(GuideRotation.OnPointerDown), new[] { typeof(PointerEventData) })]
        private static class GuideRotation_OnPointerDown_Patches
        {
            private static void Postfix() => OnGuideClick();
        }

        [HarmonyPatch(typeof(GuideScale), nameof(GuideScale.OnPointerDown), new[] { typeof(PointerEventData) })]
        private static class GuideScale_OnPointerDown_Patches
        {
            private static void Postfix() => OnGuideClick();
        }
#if FEATURE_AUTOGEN
        [HarmonyPatch(typeof(PauseCtrl.FileInfo), "Apply", typeof(OCIChar))]
        private static class PauseCtrl_Apply_Patches
        {
            private static bool Prefix(PauseCtrl.FileInfo __instance, OCIChar _char)
            {
                Stop();
                _self.isAutoGenerating = false;
                return true;
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                _self._ui.gameObject.SetActive(false);
                return true;
            }
        }
#endif
        private static class OCI_OnDelete_Patches
        {
#if IPA
            public static void ManualPatch(HarmonyInstance harmony)
#elif BEPINEX
            public static void ManualPatch(Harmony harmony)
#endif
            {
                IEnumerable<Type> ociTypes = Assembly.GetAssembly(typeof(ObjectCtrlInfo)).GetTypes().Where(myType => myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(ObjectCtrlInfo)));
                foreach(Type t in ociTypes)
                {
                    try
                    {
                        harmony.Patch(t.GetMethod("OnDelete", AccessTools.all), new HarmonyMethod(typeof(OCI_OnDelete_Patches).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
                    }
                    catch (Exception e)
                    {
                        Logger.LogWarning("Could not patch OnDelete of type " + t.Name + "\n" + e);
                    }
                }
            }

            private static void Prefix(object __instance)
            {
                ObjectCtrlInfo oci = __instance as ObjectCtrlInfo;
                if (oci != null)
                    _self.RemoveInterpolables(_self._interpolables.Where(i => i.Value.oci == oci).Select(i => i.Value).ToList());
            }
        }

#if FEATURE_AUTOGEN

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                if(Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0) {
                    _self._selectedOCI = null;
#if FIXED_096_DEBUG
                    _self.SetObjectCtrlDirty(null);
#endif
                    _self.UpdateInterpolablesView();
                    _self.UpdateKeyframeWindow(false);

                    foreach (Transform child in _self._keyframesContainer.transform)
                    {
                        child.gameObject.SetActive(false);
                    }
                }

                return true;
            }
        }


        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = null;
                if (Singleton<Studio.Studio>.Instance.dicInfo.TryGetValue(_node, out objectCtrlInfo))
                {   

                    int selectedOciHashCode = 0;
                    int newOciHashCode = objectCtrlInfo.GetHashCode();

                    if (_self._selectedOCI != null) {
                        selectedOciHashCode = _self._selectedOCI.GetHashCode();
                    }

                    if (selectedOciHashCode == newOciHashCode) {
                        return true;
                    }

                    _self._selectedOCI = objectCtrlInfo;
#if FIXED_096_DEBUG
                    if (!_self._ociControlMgmt.ContainsKey(newOciHashCode)) {

                        ObjectCtrlItem objectCtrlItem = new ObjectCtrlItem();
                        GameObject keyframeGroup = new GameObject($"name_{newOciHashCode}");
                        keyframeGroup.transform.SetParent(_self._keyframesContainer, false);
                        keyframeGroup.transform.localPosition = Vector3.zero;
                        objectCtrlItem.keyframeGroup = keyframeGroup;
                        
                        objectCtrlItem.displayedKeyframes = new List<KeyframeDisplay>();
                        objectCtrlItem.oci = objectCtrlInfo;
                        objectCtrlItem.dirty = true;
                        _self._ociControlMgmt.Add(newOciHashCode, objectCtrlItem);
                    }

                    foreach (Transform child in _self._keyframesContainer.transform)
                    {
                        if (child.gameObject.name.Contains("name_"))
                        {
                            Transform childTransform = child.gameObject.transform;
                            Vector3 pos = childTransform.localPosition;
                            pos.y = -10000f; // 원하는 y 값으로 변경
                            childTransform.localPosition = pos;
                        }
                    }
#endif
#if FIXED_096_DEBUG
                    _self.SetObjectCtrlDirty(_self._selectedOCI);
#endif
                    _self.UpdateInterpolablesView();
                    _self.UpdateKeyframeWindow(false);                               
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance) {

                TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes[0];
                ObjectCtrlInfo objectCtrlInfo = null;

                if (Singleton<Studio.Studio>.Instance.dicInfo.TryGetValue(_node, out objectCtrlInfo))
                {
                    _self._selectedOCI = objectCtrlInfo;
#if FIXED_096_DEBUG
                    _self.SetObjectCtrlDirty(_self._selectedOCI);
#endif                    
                    _self.UpdateInterpolablesView();
                    _self.UpdateKeyframeWindow(false);                              
                }

                return true;
            }
        }        
#endif

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnClickDelete))]
        internal static class WorkspaceCtrl_OnClickDelete_Patches
        {
            private static bool Prefix()
            {
                // Prevent people from deleting objects in studio workspace by accident while timeline window is in focus
                if (Input.GetKey(KeyCode.Delete))
                    return !_self._ui.gameObject.activeSelf;
                return true;
            }
        }

#if KOIKATSU
        [HarmonyPatch(typeof(ShortcutKeyCtrl), "Update")]
        private static class ShortcutKeyCtrl_Update_Patches
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> instructionList = instructions.ToList();
                for (int i = 0; i < instructionList.Count; i++)
                {
                    CodeInstruction instruction = instructionList[i];
                    if (i != 0 && instruction.opcode == OpCodes.Call && instructionList[i - 1].opcode == OpCodes.Ldc_I4_S && (sbyte)instructionList[i - 1].operand == 99)
                        yield return new CodeInstruction(OpCodes.Call, typeof(ShortcutKeyCtrl_Update_Patches).GetMethod(nameof(PreventKeyIfCtrl), BindingFlags.NonPublic | BindingFlags.Static));
                    else
                        yield return instruction;
                }
            }

            private static bool PreventKeyIfCtrl(KeyCode key)
            {
                return Input.GetKey(KeyCode.LeftControl) == false && Input.GetKeyDown(key);
            }
        }
#endif
#endregion
    }
}
