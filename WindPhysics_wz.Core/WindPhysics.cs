using Studio;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using ToolBox;
using ToolBox.Extensions;
using UILib;
using UnityEngine;
using UnityEngine.SceneManagement;

#if IPA
using Harmony;
using IllusionPlugin;
#elif BEPINEX
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
#endif
#if AISHOUJO || HONEYSELECT2
using CharaUtils;
using ExtensibleSaveFormat;
using AIChara;
using System.Security.Cryptography;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
using UnityEx.Misc;
#endif


namespace WindPhysics
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class WindPhysics : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "WindPhysics";
        public const string Version = "0.9.6.0";
        public const string GUID = "com.alton.illusionplugins.wind";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "wind_physics";
#endif
        #endregion

#if IPA
        public override string Name { get { return _name; } }
        public override string Version { get { return _version; } }
        public override string[] Filter { get { return new[] { "StudioNEO_32", "StudioNEO_64" }; } }
#endif

        #region Private Types
        #endregion

        #region Private Variables

        internal static new ManualLogSource Logger;
        internal static WindPhysics _self;

        private static string _assemblyLocation;
        private bool _loaded = false;

        // internal List<ObjectCtrlInfo> _selectedOCIs = new List<ObjectCtrlInfo>();

        // internal Dictionary<int, WindData> _ociObjectMgmt = new Dictionary<int, WindData>();

        // private Coroutine _IterativeRoutine;

        private float timeElapsed = 0f;

        private WindZone _windZone;

        // Config
        internal static ConfigEntry<bool> ConfigKeyEnableWind { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableWindShortcut { get; private set; }

        // Environment    
        internal static ConfigEntry<float> WindAngle { get; private set; }        
        internal static ConfigEntry<float> WindCycle { get; private set; }
        internal static ConfigEntry<float> WindForce { get; private set; }
        internal static ConfigEntry<float> WindUpForce { get; private set; }
        internal static ConfigEntry<float> WindForceVariant { get; private set; }

        internal static ConfigEntry<float> AccesoriesForce { get; private set; }
        internal static ConfigEntry<float> AccesoriesDamping { get; private set; }
        internal static ConfigEntry<float> AccesoriesStiffness { get; private set; }

        internal static ConfigEntry<float> HairForce { get; private set; }
        internal static ConfigEntry<float> HairDamping { get; private set; }
        internal static ConfigEntry<float> HairStiffness { get; private set; }
        
        internal static ConfigEntry<float> ItemForce { get; private set; }
        internal static ConfigEntry<float> ItemDamping { get; private set; }
        internal static ConfigEntry<float> ItemStiffness { get; private set; }

        internal static ConfigEntry<float> ClotheForce { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }

        #endregion

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            // environment
            WindForce = Config.Bind("All", "Force", 1f, new ConfigDescription("wind force", new AcceptableValueRange<float>(1f, 100.0f)));

            WindForceVariant = Config.Bind("All", "Variant", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 10.0f)));      

            WindUpForce = Config.Bind("All", "UpForce", 0.0f, new ConfigDescription("wind up or down", new AcceptableValueRange<float>(0.0f, 50.0f)));

            WindAngle = Config.Bind("All", "Angle", 0f, new ConfigDescription("wind 360", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindCycle = Config.Bind("All", "Cycle", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 10.0f)));      
        

            // accesories
            AccesoriesForce = Config.Bind("Misc", "Force", 1.0f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            AccesoriesDamping = Config.Bind("Misc", "Damping", 0.7f, new ConfigDescription("wind damping applied to accesories", new AcceptableValueRange<float>(0.0f, 1.0f)));

            AccesoriesStiffness = Config.Bind("Misc", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to accesories", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // clothes
            ClotheForce = Config.Bind("Cloth", "Force", 1.0f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ClothDamping = Config.Bind("Cloth", "Damping", 0.5f, new ConfigDescription("wind damping applied to clothes", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to clothes", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // clothes
            ItemForce = Config.Bind("Item", "Force", 1.0f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ItemDamping = Config.Bind("Item", "Damping", 0.5f, new ConfigDescription("wind damping applied to item", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ItemStiffness = Config.Bind("Item", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to item", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // hair
            HairForce = Config.Bind("Hair", "Force", 1.0f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            HairDamping = Config.Bind("Hair", "Damping", 0.5f, new ConfigDescription("wind damping applied to hairs", new AcceptableValueRange<float>(0.0f, 1.0f)));

            HairStiffness = Config.Bind("Hair", "Stiffness", 1.0f, new ConfigDescription("wind stiffness applied to hairs", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // 
            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());                     

            _windZone = Studio.Studio.instance.transform.GetComponent<WindZone>();

            if (_windZone == null)
            {
                _windZone = Studio.Studio.instance.transform.gameObject.AddComponent<WindZone>();                
            }            
        }

#if SUNSHINE || HONEYSELECT2 || AISHOUJO
        protected override void LevelLoaded(Scene scene, LoadSceneMode mode)
        {
            base.LevelLoaded(scene, mode);
            if (mode == LoadSceneMode.Single && scene.buildIndex == 2)
                Init();
        }
#endif

        protected override void Update()
        {
            if (_loaded == false)
                return;


            timeElapsed += Time.deltaTime;

            if (ConfigKeyEnableWindShortcut.Value.IsDown())
            {
                if (ConfigKeyEnableWind.Value == false) {

                    for (int idx=0; idx<30; idx++) {
                        TreeNodeObject nodeObj = Singleton<Studio.Studio>.Instance.treeNodeCtrl.GetNode(idx);

                        if(nodeObj != null) {
                             ObjectCtrlInfo objectCtrlInfo = null;
                            if (Singleton<Studio.Studio>.Instance.dicInfo.TryGetValue(nodeObj, out objectCtrlInfo)) {                                
                                OCIChar character = objectCtrlInfo as OCIChar;
                                if (character != null) {
                                    Logic.SetCharacter(character);
                                }

                                OCIItem item = objectCtrlInfo as OCIItem;
                                if (item != null) {
                                    Logic.SetItem(item);
                                }
                            }
                        }
                    }

                } else {
                    EnableWindEffect(false, 0f);
                } 
                ConfigKeyEnableWind.Value = !ConfigKeyEnableWind.Value;
            }

            if (_windZone != null && _windZone.gameObject.activeSelf) {
                float cycleProgress = Mathf.PingPong(timeElapsed, WindCycle.Value) / WindCycle.Value;

                // 바람 세기를 주기적으로 변화시킵니다.
                float currentWindStrength = WindForce.Value + Mathf.Sin(cycleProgress * 2 * Mathf.PI) * WindForceVariant.Value;

                EnableWindEffect(true, currentWindStrength);           
            }
        }
        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            // UIUtility.Init();
            _loaded = true;
        }

        private void SceneInit()
        {
        }

        internal void EnableWindEffect(bool enable, float strength) {
            _windZone.SetActive(enable);

            if (enable) {
                float radianAngle = WindAngle.Value * Mathf.Deg2Rad;

                Vector3 windDirection = new Vector3(Mathf.Sin(radianAngle), 0f, Mathf.Cos(radianAngle));
                // 벡터를 정규화합니다. (크기를 1로 만듭니다.)
                windDirection.Normalize();
                _windZone.direction = windDirection;
                _windZone.windMain = WindForce.Value;
                _windZone.radius = WindAngle.Value;
            }
        }
        #endregion

        #region Patches

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self.SceneInit();
                return true;
            }
        }

        #endregion
    }       
}