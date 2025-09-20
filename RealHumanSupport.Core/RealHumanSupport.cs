using Studio;
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
// using UnityEngine.Networking;

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
using KKAPI.Studio;

#endif

#if AISHOUJO || HONEYSELECT2
using AIChara;
#endif


namespace RealHumanSupport
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if KOIKATSU || SUNSHINE
    [BepInProcess("CharaStudio")]
#elif AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class RealHumanSupport : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "RealHumanSupport";
        public const string Version = "0.9.0";
        public const string GUID = "com.alton.illusionplugins.RealHuman";
        internal const string _ownerId = "RealHumanSupport";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "RealHuman_support";
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
        internal static RealHumanSupport _self;

        private static string _assemblyLocation;
        private bool _loaded = false;

        private Coroutine _breathCoroutine;

        internal static ConfigEntry<bool> EyeEarthQuakeActive { get; private set; }

        internal static ConfigEntry<bool> WetDropActive { get; private set; }

        internal static ConfigEntry<bool> LiquidDropActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }

        internal static ConfigEntry<float> WetPeriod { get; private set; }        
        internal static ConfigEntry<float> Amplitude { get; private set; }
        internal static ConfigEntry<float> Speed { get; private set; }

        private ObjectCtrlInfo _selectedOCI;

        private Material _cf_m_skin_body_00;
        private Material _c_m_liquid_body;
        private Material _cf_m_skin_head_01;
        private List<Material> _c_m_eye_01s = new List<Material>();

        private float _defaultBodyBumpScale1 = 0.0f;
        private float _defaultBodyBumpScale2 = 0.0f;

        private BODY_SHADER _bodyShaderType = BODY_SHADER.DEFAULT;
        private BODY_SHADER _faceShaderType = BODY_SHADER.DEFAULT;

        internal enum BODY_SHADER
        {
            HANMAN,
            DEFAULT
        }

        #endregion

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            EyeEarthQuakeActive  = Config.Bind("Option", "Eye EarthQuake Active", false, new ConfigDescription("Enable/Disable"));

            WetDropActive = Config.Bind("Option", "Wet Active", false, new ConfigDescription("Enable/Disable"));

            LiquidDropActive = Config.Bind("Option", "Liquid Active", false, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind("Option", "Breath Active", false, new ConfigDescription("Enable/Disable"));

            WetPeriod = Config.Bind("Wet", "Period", 4.0f, new ConfigDescription("Wet Period", new AcceptableValueRange<float>(1.0f, 10.0f)));

            Amplitude = Config.Bind("Breath", "Amplitude", 0.5f, new ConfigDescription("Breath Amplitude", new AcceptableValueRange<float>(0.0f, 1.0f)));

            Speed = Config.Bind("Breath", "Speed", 1.5f, new ConfigDescription("Breath Speed", new AcceptableValueRange<float>(0.1f, 10.0f)));

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            _breathCoroutine = StartCoroutine(BreathingRoutine());
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
        }

        private void PostLateUpdate()
        {
        }
        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;
        }

        private IEnumerator BreathingRoutine()
        {

            float wetMin = 0.05f;
            float wetMax = 0.14f;
            while (true)
            {
                if (_loaded == true && _cf_m_skin_body_00 != null)
                {
                    float time = Time.time;

                    if (EyeEarthQuakeActive.Value == true)
                    {
                        foreach (Material mat in _c_m_eye_01s)
                        {
                            // sin 파형 (0 ~ 1로 정규화)
                            float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                            float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                            mat.SetFloat("_Texture4Rotator", eyeScale);

                            eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                            mat.SetFloat("_Parallax", eyeScale);
                        }                        
                    }

                    if (WetDropActive.Value == true)
                    {
                        float t = (time % WetPeriod.Value) / WetPeriod.Value;
                        // 1️⃣ WetBumpStreaks : 한 방향으로 내려가는 흐름
                        float easedBump = Mathf.Sin(t * Mathf.PI * 1.0f); 
                        float wetBumpScale = Mathf.Lerp(wetMin, wetMax, easedBump);

                        if (_bodyShaderType == BODY_SHADER.HANMAN)
                        {
                            _cf_m_skin_body_00.SetFloat("_WetBumpStreaks", wetBumpScale);
                        }

                        if (_faceShaderType == BODY_SHADER.HANMAN)
                        {
                            _cf_m_skin_head_01.SetFloat("_WetBumpStreaks", wetBumpScale);                                           
                        }                        
                    }

                    if (LiquidDropActive.Value)
                    {
                        // ------------------------------
                        // 2️⃣ UV Scroll (상하 천천히)
                        // ------------------------------
                        Vector4 uv = _c_m_liquid_body.GetVector("_WeatheringUV");
                        uv.y += Time.deltaTime * 0.025f; // 전체 흐름 느리게
                        _c_m_liquid_body.SetVector("_WeatheringUV", uv);
                        if (_cf_m_skin_head_01 != null)
                            _cf_m_skin_head_01.SetVector("_WeatheringUV", uv);
                    }

                    if (BreathActive.Value)
                    {
                        // ------------------------------
                        // 3️⃣ NormalMap
                        // ------------------------------
                        float sinValue = (Mathf.Sin(time * Speed.Value) + 1f) * 0.5f;
                        float bumpScale1 = Mathf.Lerp(_defaultBodyBumpScale1, _defaultBodyBumpScale1 + Amplitude.Value, sinValue);

                        float bumpScale2 = Mathf.Lerp(_defaultBodyBumpScale2 - 0.2f, _defaultBodyBumpScale2 + 0.7f, sinValue);

                        _cf_m_skin_body_00.SetFloat("_BumpScale", bumpScale1);
                        _cf_m_skin_body_00.SetFloat("_BumpScale2", bumpScale2);
                      
                        
                        if (_cf_m_skin_head_01 != null)
                        {
                            _cf_m_skin_body_00.SetFloat("_BumpScale", bumpScale1);
                            _cf_m_skin_head_01.SetFloat("_BumpScale2", bumpScale2 / 2);                        
                        }
                    }

                    yield return null;
                }
                else
                {
                    yield return new WaitForSeconds(1);
                }
            }
        }

        #endregion

        #region Public Methods
        #endregion

        #region Patches

        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            AddRealFluidEffect(ociChar);
        }

        private static void AddRealFluidEffect(OCIChar ociChar)
        {
            _self._selectedOCI = ociChar;
            SkinnedMeshRenderer[] sks = _self._selectedOCI.guideObject.transformTarget.GetComponentsInChildren<SkinnedMeshRenderer>();

            _self._c_m_eye_01s.Clear();
            foreach (SkinnedMeshRenderer render in sks.ToList())
            {
                // Material mat = render.material;
                // Texture normalMap = mat.GetTexture("_BumpMap");
                // UnityEngine.Debug.Log("Normal map: " + (normalMap != null ? normalMap.name : "None"));

                foreach (var mat in render.sharedMaterials)
                {
                    if (mat.name.ToLower().Contains("cf_m_skin_body_00"))
                    {
                        _self._cf_m_skin_body_00 = render.material;
                        _self._defaultBodyBumpScale1 = _self._cf_m_skin_body_00.GetFloat("_BumpScale");
                        _self._defaultBodyBumpScale2 = _self._cf_m_skin_body_00.GetFloat("_BumpScale2");

                        UnityEngine.Debug.Log($"_defaultBodyBumpScale1: + {_self._defaultBodyBumpScale1}");
                        UnityEngine.Debug.Log($"_defaultBodyBumpScale2: + {_self._defaultBodyBumpScale2}");

                        if (mat.shader.name.Contains("Hanmen/Next-Gen Body"))
                        {
                            _self._bodyShaderType = BODY_SHADER.HANMAN;
                            _self._cf_m_skin_body_00.SetFloat("_WetBumpStreaks", 0.1f);
                            _self._cf_m_skin_body_00.SetFloat("_ExGloss", 0.8f);
                            _self._cf_m_skin_body_00.SetFloat("_Gloss", 0.1f);
                        }
                        else
                        {
                            _self._bodyShaderType = BODY_SHADER.DEFAULT;
                        }

                    }

                    if (mat.name.ToLower().Contains("cf_m_skin_head_01"))
                    {
                        _self._cf_m_skin_head_01 = render.material;

                        if (mat.shader.name.Contains("Hanmen/Next-Gen Face"))
                        {
                            _self._faceShaderType = BODY_SHADER.HANMAN;
                            _self._cf_m_skin_head_01.SetFloat("_WetBumpStreaks", 0.1f);
                            _self._cf_m_skin_head_01.SetFloat("_ExGloss", 0.7f);
                            _self._cf_m_skin_head_01.SetFloat("_Gloss", 0.1f);
                        }
                        else
                        {
                            _self._faceShaderType = BODY_SHADER.DEFAULT;
                        }
                    }

                    if (mat.name.ToLower().Contains("c_m_eye_01"))
                    {
                        _self._c_m_eye_01s.Add(render.material);
                    }

                    if (mat.shader.name == "AIT/Liquid")
                    {
                        _self._c_m_liquid_body = mat;
                    }

                    // UnityEngine.Debug.Log($"share name: + {mat.shader.name}");
                }
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {

            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);

                OCIChar ociChar = objectCtrlInfo as OCIChar;
                if (ociChar != null)
                {                                       
                    ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar));
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {

                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes[0]);

                OCIChar ociChar = objectCtrlInfo as OCIChar;
                if (ociChar != null)
                {
                    ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar));
                }
                return true;
            }
        }
        
       // 악세러리 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        private static class ChaControl_ChangeAccessory_Patches
        {
            private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                if (ociChar != null)
                {
                    __instance.StartCoroutine(ExecuteAfterFrame(ociChar));
                }                
            } 
        }
        
        // 옷 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                if (ociChar != null)
                {
                    __instance.StartCoroutine(ExecuteAfterFrame(ociChar));
                }                   
            }
        }

        // 옷 전체 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                if (ociChar != null)
                {
                    __instance.StartCoroutine(ExecuteAfterFrame(ociChar));
                } 
            }
        }


        // [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        // internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        // {
        //     private static bool Prefix(object __instance, TreeNodeObject _node)
        //     {
        //         if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
        //         {
        //         }

        //         return true;
        //     }
        // }
        #endregion
    }
}
