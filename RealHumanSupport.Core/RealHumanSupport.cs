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
using UnityEngine.Rendering;
using KK_PregnancyPlus;
using System.Threading.Tasks;

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
using ADV.Commands.Camera;
using KKAPI.Studio;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
#endif
using RootMotion.FinalIK;

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

        internal static ConfigEntry<KeyboardShortcut> ConfigTestShortcut { get; private set; }


        internal static ConfigEntry<bool> EyeShakeActive { get; private set; }

        internal static ConfigEntry<bool> ExtraDynamicBoneColliderActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }

        internal static ConfigEntry<bool> FaceBumpActive { get; private set; }

        internal static ConfigEntry<bool> BodyBumpActive { get; private set; }


        // breath
        internal static ConfigEntry<float> BreathAmplitude { get; private set; }

        internal static ConfigEntry<float> BreathInterval { get; private set; }

        public static ConfigEntry<bool> AddCheekbone  { get; private set; }

        private float moveThreshold = 0.05f; // 5cm 이상 움직이면 변화 감지
        private float backThreshold = -0.2f; // 뒤쪽 판정 기준 (m)
        private float frontThreshold = 0.2f; // 앞쪽 판정 기준 (m)
        private float kickHeightThreshold = 0.05f; // 발차기 높이 기준 (m)


        private Vector3 prevLeftLocalPos;
        private Vector3 prevRightLocalPos;
        
        private OCIChar _selectedOciChar;

        // end
        private Texture2D _faceExpressionFemaleBumpMap2;

        private Texture2D _faceExpressionMaleBumpMap2;
        
        private Texture2D _bodyStrongFemaleBumpMap2;

        private Texture2D _bodyStrongMaleBumpMap2;

        private int _mouth_type;
        private int _eye_type;

        private AssetBundle _bundle;


        private List<ObjectCtrlInfo> _selectedOCIs = new List<ObjectCtrlInfo>();

        private Dictionary<int, RealHumanData> _ociCharMgmt = new Dictionary<int, RealHumanData>();
        private Dictionary<int, RealFaceData> _faceMouthMgmt = new Dictionary<int, RealFaceData>();
        private Dictionary<int, RealFaceData> _faceEyesMgmt = new Dictionary<int, RealFaceData>();
        private Coroutine _oneSecondRoutine;

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            // UnityEngine.Debug.Log($">> Awake");

            base.Awake();

            ConfigTestShortcut = Config.Bind("ShortKey", "Test key", new KeyboardShortcut(KeyCode.O, KeyCode.LeftShift));

            EyeShakeActive = Config.Bind("Option", "Eye Shake Active", false, new ConfigDescription("Enable/Disable"));

            ExtraDynamicBoneColliderActive = Config.Bind("Option", "Extra DynamicBone Active", false, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind("Option", "Real Breath Active", false, new ConfigDescription("Enable/Disable"));

            FaceBumpActive = Config.Bind("Option", "Real Face Active", false, new ConfigDescription("Enable/Disable"));

            BodyBumpActive = Config.Bind("Option", "Real Body Active", false, new ConfigDescription("Enable/Disable"));

            BreathInterval = Config.Bind("Breath", "Cycle", 1.5f, new ConfigDescription("Breath Interval", new AcceptableValueRange<float>(1.0f, 10.0f)));

            BreathAmplitude = Config.Bind("Breath", "Amplitude", 0.5f, new ConfigDescription("Breath Amplitude", new AcceptableValueRange<float>(0.1f, 1.0f)));

            AddCheekbone = Config.Bind("Test", "Test CheckBone", false, new ConfigDescription("Enable/Disable"));      

            
            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            _oneSecondRoutine = StartCoroutine(OneSecondRoutine());
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

            if (ConfigTestShortcut.Value.IsDown())
            {
                // UnityEngine.Debug.Log($">> ConfigTestShortcut press");

                if (_selectedOciChar != null)
                {
                    OCIChar ociChar = _selectedOciChar as OCIChar;
             
                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        if (realHumanData.cf_m_skin_head == null || realHumanData.cf_m_skin_body == null)
                        {
                            InitRealHumanData(ociChar, realHumanData);
                        }

                        // UnityEngine.Debug.Log($">> ociChar {ociChar.GetHashCode()}, realHumanData.cf_m_skin_body {realHumanData.cf_m_skin_body}");
                        DoBodyRealEffect(ociChar, realHumanData);
                    }
                }
            }

        }
        #endregion

        #region Private Methods
        private void Init()
        {
            // UnityEngine.Debug.Log($">> Init");

            UIUtility.Init();
            _loaded = true;

            // 번들 파일 경로
            string bundlePath = Application.dataPath + "/../abdata/realgirl/realgirlbundle.unity3d";

            // string bundlePath = Path.Combine(pluginDir, "realgirlbundle.unity3d");
            if (!File.Exists(bundlePath))
            {
                UnityEngine.Debug.Log($">> AssetBundle not found at: {bundlePath}");
                return;
            }

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null)
            {
                UnityEngine.Debug.Log(">> Failed to load AssetBundle!");
                return;
            }

            _bodyStrongMaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_M_BumpMap2");
            _bodyStrongFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_F_BumpMap2");
            
            _faceExpressionFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_F_BumpMap2");
            _faceExpressionMaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_M_BumpMap2");

            _faceMouthMgmt.Add(0, new RealFaceData());
            _faceMouthMgmt.Add(1, new RealFaceData(new BArea(512, 610, 120, 80, 0.2f)));
            _faceMouthMgmt.Add(2, new RealFaceData(new BArea(512, 610, 140, 90, 0.2f)));
            _faceMouthMgmt.Add(3, new RealFaceData());
            _faceMouthMgmt.Add(4, new RealFaceData(new BArea(512, 690, 60, 50, 0.4f)));
            _faceMouthMgmt.Add(5, new RealFaceData(new BArea(512, 690, 60, 50, 0.4f)));
            _faceMouthMgmt.Add(6, new RealFaceData(new BArea(512, 690, 60, 50, 0.3f)));
            _faceMouthMgmt.Add(7, new RealFaceData(new BArea(470, 590, 60, 40)));
            _faceMouthMgmt.Add(8, new RealFaceData(new BArea(570, 590, 60, 40)));
            _faceMouthMgmt.Add(9, new RealFaceData(new BArea(512, 590, 100, 60)));
            _faceMouthMgmt.Add(10, new RealFaceData(new BArea(520, 630, 40, 40), new BArea(320, 660, 80, 120, 1f), new BArea(690, 660, 80, 120, 1f)));
            _faceMouthMgmt.Add(11, new RealFaceData(new BArea(520, 630, 40, 40), new BArea(320, 660, 80, 120, 1f), new BArea(690, 660, 80, 120, 1f)));
            _faceMouthMgmt.Add(12, new RealFaceData(new BArea(512, 690, 90, 60, 0.5f)));
            _faceMouthMgmt.Add(13, new RealFaceData(new BArea(320, 660, 80, 120, 1.5f), new BArea(690, 660, 80, 120, 1.5f)));
            _faceMouthMgmt.Add(14, new RealFaceData());
            _faceMouthMgmt.Add(15, new RealFaceData(new BArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(16, new RealFaceData(new BArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(17, new RealFaceData(new BArea(320, 660, 80, 120, 2f), new BArea(690, 660, 80, 120, 2f)));
            _faceMouthMgmt.Add(18, new RealFaceData(new BArea(320, 660, 80, 120, 2f), new BArea(690, 660, 80, 120, 2f)));
            _faceMouthMgmt.Add(19, new RealFaceData());
            _faceMouthMgmt.Add(20, new RealFaceData());
            _faceMouthMgmt.Add(21, new RealFaceData(new BArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(22, new RealFaceData());
            _faceMouthMgmt.Add(23, new RealFaceData(new BArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(24, new RealFaceData(new BArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(25, new RealFaceData());           

            _faceEyesMgmt.Add(0, new RealFaceData());
            _faceEyesMgmt.Add(1, new RealFaceData(new BArea(455, 505, 60, 50, 0.4f), new BArea(580, 505, 60, 50, 0.4f), new BArea(400, 460, 80, 60, 0.4f), new BArea(630, 460, 80, 60, 0.4f)));
            _faceEyesMgmt.Add(2, new RealFaceData(new BArea(470, 500, 60, 50, 0.4f), new BArea(560, 500, 60, 50, 0.4f), new BArea(400, 460, 80, 60, 0.4f), new BArea(630, 460, 80, 60, 0.4f)));
            _faceEyesMgmt.Add(3, new RealFaceData());
            _faceEyesMgmt.Add(4, new RealFaceData());
            _faceEyesMgmt.Add(5, new RealFaceData());
            _faceEyesMgmt.Add(6, new RealFaceData());
            _faceEyesMgmt.Add(7, new RealFaceData(new BArea(455, 505, 60, 50, 0.4f), new BArea(580, 505, 60, 50, 0.4f), new BArea(310, 470, 60, 40, 0.4f), new BArea(700, 470, 60, 40, 0.4f)));
            _faceEyesMgmt.Add(8, new RealFaceData(new BArea(455, 505, 60, 50, 0.4f), new BArea(580, 505, 60, 50, 0.4f), new BArea(310, 470, 60, 40, 0.4f))); 
            _faceEyesMgmt.Add(9, new RealFaceData(new BArea(455, 505, 60, 50, 0.4f), new BArea(580, 505, 60, 50, 0.4f), new BArea(700, 470, 60, 40, 0.4f)));
            _faceEyesMgmt.Add(10, new RealFaceData());
            _faceEyesMgmt.Add(11, new RealFaceData());
            _faceEyesMgmt.Add(12, new RealFaceData(new BArea(310, 470, 60, 40, 0.4f), new BArea(455, 505, 60, 50, 0.4f)));
            _faceEyesMgmt.Add(13, new RealFaceData(new BArea(700, 470, 60, 40, 0.4f), new BArea(580, 505, 60, 50, 0.4f)));
        }

        private void SceneInit()
        {
            // UnityEngine.Debug.Log($">> SceneInit()");
            foreach (var kvp in _ociCharMgmt)
            {
                var key = kvp.Key;
                RealHumanData value = kvp.Value;
                value.c_m_eye.Clear();
                if (value.coroutine != null)
                    value.ociChar.charInfo.StopCoroutine(value.coroutine);
            }

            _mouth_type = 0;
            _eye_type = 0;

            _selectedOCIs.Clear();
            _ociCharMgmt.Clear();
        }

        IEnumerator OneSecondRoutine()
        {
            while (true) // 무한 반복
            {
                foreach(ObjectCtrlInfo ctrlInfo in _selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        if (!Input.GetMouseButton(0)) {
                            Vector3 cur_lk_left_foot_rot = GetBoneRotationFromIK(realHumanData.lk_left_foot_bone);
                            Vector3 cur_lk_right_foot_rot = GetBoneRotationFromIK(realHumanData.lk_right_foot_bone);

                            Vector3 cur_fk_left_foot_rot = GetBoneRotationFromFK(realHumanData.fk_left_foot_bone);
                            Vector3 cur_fk_right_foot_rot = GetBoneRotationFromFK(realHumanData.fk_right_foot_bone);                            
                            Vector3 cur_fk_left_thigh_rot = GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone);
                            Vector3 cur_fk_right_thigh_rot = GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone);
                            Vector3 cur_fk_left_knee_rot = GetBoneRotationFromFK(realHumanData.fk_left_knee_bone);
                            Vector3 cur_fk_right_knee_rot = GetBoneRotationFromFK(realHumanData.fk_right_knee_bone);                            
                            Vector3 cur_fk_left_shoudler_rot = GetBoneRotationFromFK(realHumanData.fk_left_shoudler_bone);
                            Vector3 cur_fk_right_shoudler_rot = GetBoneRotationFromFK(realHumanData.fk_right_shoudler_bone);
                            Vector3 cur_fk_neck_rot = GetBoneRotationFromFK(realHumanData.fk_neck_bone);
                            Vector3 cur_fk_spine01_rot = GetBoneRotationFromFK(realHumanData.fk_spine01_bone);
                            Vector3 cur_fk_spine02_rot = GetBoneRotationFromFK(realHumanData.fk_spine02_bone);

                            if (
                                (cur_lk_left_foot_rot != realHumanData.prev_lk_left_foot_rot) ||
                                (cur_lk_right_foot_rot != realHumanData.prev_lk_right_foot_rot) ||
                                (cur_fk_left_foot_rot != realHumanData.prev_fk_left_foot_rot) ||
                                (cur_fk_right_foot_rot != realHumanData.prev_fk_right_foot_rot) ||                                
                                (cur_fk_left_thigh_rot != realHumanData.prev_fk_left_thigh_rot) ||
                                (cur_fk_right_thigh_rot != realHumanData.prev_fk_right_thigh_rot) ||
                                (cur_fk_left_knee_rot != realHumanData.prev_fk_left_knee_rot) ||
                                (cur_fk_right_knee_rot != realHumanData.prev_fk_right_knee_rot) ||                                
                                (cur_fk_left_shoudler_rot != realHumanData.prev_fk_left_shoudler_rot) ||
                                (cur_fk_right_shoudler_rot != realHumanData.prev_fk_right_shoudler_rot) ||
                                (cur_fk_neck_rot != realHumanData.prev_fk_neck_rot) ||
                                (cur_fk_spine01_rot != realHumanData.prev_fk_spine01_rot) ||
                                (cur_fk_spine02_rot != realHumanData.prev_fk_spine02_rot)
                            )
                            {
                                DoBodyRealEffect(ociChar, realHumanData);
                            }    
                        }
                    }
                }            
                    
                yield return new WaitForSeconds(1.0f); // 1.5초 대기
            }
        }

        private IEnumerator Routine(RealHumanData realHumanData)
        {
            float wetMin = 0.05f;
            float wetMax = 0.14f;
            while (true)
            {
                if (_loaded == true)
                {
                    float time = Time.time;
                    if (EyeShakeActive.Value == true)
                    {
                        foreach (Material mat in realHumanData.c_m_eye)
                        {
                            // sin 파형 (0 ~ 1로 정규화)
                            float easedBump = (Mathf.Sin(time * Mathf.PI * 3.5f * 2f) + 1f) * 0.5f;

                            float eyeScale = Mathf.Lerp(0.18f, 0.21f, easedBump);
                            mat.SetFloat("_Texture4Rotator", eyeScale);

                            eyeScale = Mathf.Lerp(0.1f, 0.2f, easedBump);
                            mat.SetFloat("_Parallax", eyeScale);
                        }
                    }

                    if (BreathActive.Value)
                    {
                        float sinValue = (Mathf.Sin(time * BreathInterval.Value) + 1f) * 0.5f;
                        
                        foreach (var ctrl in StudioAPI.GetSelectedControllers<PregnancyPlusCharaController>())
                        {
                            ctrl.infConfig.SetSliders(BellyTemplate.GetTemplate(1));
                            ctrl.infConfig.inflationSize = (1f - sinValue) * 10f * BreathAmplitude.Value;
                            ctrl.MeshInflate(new MeshInflateFlags(ctrl), "StudioSlider");
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

        private static void SaveAsPNG(Texture2D tex, string path)
        {
            if (tex == null)
            {
                Debug.LogError("Texture is null, cannot save.");
                return;
            }

            byte[] bytes = tex.EncodeToPNG();
            try
            {
                File.WriteAllBytes(path, bytes);
                Debug.Log($"Texture saved to: {path}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save texture: {e}");
            }
        }
    
        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar, RealHumanData realHumanData)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;
            
            // SupportEyeShake(ociChar, realHumanData);
            // SupportExtraDynamicBones(ociChar, realHumanData);
        }

        private static Texture2D MakeReadableTexture(Texture2D texture)
        {
            RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 24, RenderTextureFormat.ARGB32);

            RenderTexture cachedActive = RenderTexture.active;
            RenderTexture.active = rt;
            Graphics.Blit(texture, rt);

            Texture2D tex = new Texture2D(texture.width, texture.height, TextureFormat.ARGB32, true);
            tex.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            tex.Apply();

            RenderTexture.active = cachedActive;
            RenderTexture.ReleaseTemporary(rt);

            return tex;
        }

        private static Texture2D SetTextureSize(Texture2D rexture, int width, int height)
        {
            int targetWidth = width;
            int targetHeight = height;

            if (rexture.width == targetWidth && rexture.height == targetHeight)
            {
                // 이미 맞는 사이즈
                return rexture;
            }

            // RenderTexture를 이용한 다운사이징
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(rexture, rt);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D resized = new Texture2D(targetWidth, targetHeight, TextureFormat.ARGB32, false);
            resized.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return resized;
        }

        private static DynamicBoneCollider AddFingerDynamicBoneCollider(Transform target)
        {
            // GameObject 생성 (Bone 자식으로)
            GameObject colliderObj = new GameObject(target.name + "_DynamicBoneCollider");
            colliderObj.transform.SetParent(target, false);
            colliderObj.transform.localPosition = Vector3.zero;
            colliderObj.transform.localRotation = Quaternion.identity;

            // Collider 추가
            var dbc = colliderObj.AddComponent<DynamicBoneCollider>();

            // 기본 세팅
            dbc.m_Radius = 0.4f;             // 손가락 충돌 반경
            dbc.m_Height = 1.1f;              // 캡슐 길이 (짧게)
            dbc.m_Direction = DynamicBoneColliderBase.Direction.X; // X축 기준
            dbc.m_Center = new Vector3(0.25f, -0.2f, 0f);
            dbc.m_Bound = DynamicBoneColliderBase.Bound.Outside;

            return dbc;
        }

        private static DynamicBoneCollider AddSpineDynamicBoneCollider(Transform target)
        {
            // GameObject 생성 (Bone 자식으로)
            GameObject colliderObj = new GameObject(target.name + "_DynamicBoneCollider");
            colliderObj.transform.SetParent(target, false);
            colliderObj.transform.localPosition = Vector3.zero;
            colliderObj.transform.localRotation = Quaternion.identity;

            // Collider 추가
            var dbc = colliderObj.AddComponent<DynamicBoneCollider>();

            // 기본 세팅
            dbc.m_Radius = 1.2f;             // 손가락 충돌 반경
            dbc.m_Height = 3f;              // 캡슐 길이 (짧게)
            dbc.m_Direction = DynamicBoneColliderBase.Direction.Y; // Y축 기준
            dbc.m_Center = new Vector3(0f, 0f, 0f);
            dbc.m_Bound = DynamicBoneColliderBase.Bound.Outside;

            return dbc;
        }

        private static void SupportExtraDynamicBones(OCIChar ociChar, RealHumanData realHumanData)
        {
            if (ExtraDynamicBoneColliderActive.Value)
            {
                // 각 dynamic bone에 y축 gravity 자동 부여
                realHumanData.leftBoob = ociChar.charInfo.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.BreastL);
                realHumanData.rightBoob = ociChar.charInfo.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.BreastR);
                realHumanData.leftButtCheek = ociChar.charInfo.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.HipL);
                realHumanData.rightButtCheek = ociChar.charInfo.GetDynamicBoneBustAndHip(ChaControlDefine.DynamicBoneKind.HipR);

                realHumanData.leftBoob.ReflectSpeed = 0.5f;
                realHumanData.leftBoob.Gravity = new Vector3(0, -0.001f, 0);
                realHumanData.leftBoob.Force = new Vector3(0, -0.0001f, 0);
                realHumanData.leftBoob.HeavyLoopMaxCount = 5;

                realHumanData.rightBoob.ReflectSpeed = 0.5f;
                realHumanData.rightBoob.Gravity = new Vector3(0, -0.001f, 0);
                realHumanData.rightBoob.Force = new Vector3(0, -0.0001f, 0);
                realHumanData.rightBoob.HeavyLoopMaxCount = 5;

                realHumanData.leftButtCheek.Gravity = new Vector3(0, -0.001f, 0);
                realHumanData.leftButtCheek.Force = new Vector3(0, -0.001f, 0);
                realHumanData.leftButtCheek.HeavyLoopMaxCount = 4;

                realHumanData.rightButtCheek.Gravity = new Vector3(0, -0.001f, 0);
                realHumanData.rightButtCheek.Force = new Vector3(0, -0.001f, 0);
                realHumanData.rightButtCheek.HeavyLoopMaxCount = 4;

                List<DynamicBoneCollider> extraColliders = new List<DynamicBoneCollider>();

                // hair/check bone에 나머지 dynamic bone collider 연결                
                DynamicBone[] hairbones = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Head").GetComponentsInChildren<DynamicBone>(true);               
                DynamicBoneCollider[] allDynamicBoneColliders = ociChar.charInfo.transform.FindLoop("cf_J_Root").GetComponentsInChildren<DynamicBoneCollider>(true);

                Transform spineObject = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Spine03");

                Transform finger2LObject = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Hand_Index02_L");
                Transform finger2RObject = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Hand_Index02_R");

                Transform finger3LObject = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Hand_Index03_L");
                Transform finger3RObject = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Hand_Index03_R");

                // hair gravity down
                foreach (DynamicBone bone in hairbones)
                {
                    //float randY1 = UnityEngine.Random.Range(-0.01f, 0.01f);
                    float randY = UnityEngine.Random.Range(-0.03f, -0.01f);
                    bone.m_Gravity = new Vector3(0f, -0.015f, 0f);
                    bone.m_Force = new Vector3(0f, randY, 0f);
                    bone.m_Damping = 0.5f;
                    bone.m_Elasticity = 0.01f;
                }

                extraColliders.Add(AddFingerDynamicBoneCollider(finger2LObject));
                extraColliders.Add(AddFingerDynamicBoneCollider(finger2RObject));
                extraColliders.Add(AddFingerDynamicBoneCollider(finger3LObject));
                extraColliders.Add(AddFingerDynamicBoneCollider(finger3RObject));

                foreach (DynamicBoneCollider collider in allDynamicBoneColliders)
                {
                    if (collider.name.Contains("Leg") || collider.name.Contains("Arm") || collider.name.Contains("Hand") || collider.name.Contains("Kosi02") || collider.name.Contains("Siri"))
                    {
                        extraColliders.Add(collider);
                    }
                }
                
                realHumanData.leftBoob.Colliders.Clear();
                foreach (var collider in extraColliders)
                    realHumanData.leftBoob.Colliders.Add(collider);

                realHumanData.rightBoob.Colliders.Clear();
                foreach (var collider in extraColliders)
                    realHumanData.rightBoob.Colliders.Add(collider);

                realHumanData.leftButtCheek.Colliders.Clear();
                foreach (var collider in extraColliders)
                    realHumanData.leftButtCheek.Colliders.Add(collider);

                realHumanData.rightButtCheek.Colliders.Clear();
                foreach (var collider in extraColliders)
                    realHumanData.rightButtCheek.Colliders.Add(collider);

                extraColliders.Add(AddSpineDynamicBoneCollider(spineObject));

                foreach (var bone in hairbones)
                {
                    foreach (var collider in extraColliders)
                        bone.m_Colliders.Add(collider);
                }

            }
        }

        private static void SupportEyeShake(OCIChar ociChar, RealHumanData realHumanData)
        {
            if (EyeShakeActive.Value)
                ociChar.charInfo.fbsCtrl.BlinkCtrl.BaseSpeed = 0.05f; // 작을수록 blink 속도가 높아짐..
            else
                ociChar.charInfo.fbsCtrl.BlinkCtrl.BaseSpeed = 0.15f;
        }

        private static Texture2D MergeRGBAlphaMaps(Texture2D rgbA, Texture2D rgbB, List<BArea> areas = null)
        {
            int w = Mathf.Min(rgbA.width, rgbB.width);
            int h = Mathf.Min(rgbA.height, rgbB.height);

            bool useArea = (areas != null && areas.Count > 0);

            Color[] cur = rgbA.GetPixels(0, 0, w, h);
            Color[] B   = rgbB.GetPixels(0, 0, w, h);

            if (!useArea)
            {
                rgbA.SetPixels(0, 0, w, h, cur);
                rgbA.Apply();
                return rgbA;
            }

            foreach (var area in areas)
            {
                float rx = (area.RadiusX > 0f ? area.RadiusX : area.RadiusY);
                float ry = (area.RadiusY > 0f ? area.RadiusY : area.RadiusX);
                if (rx <= 0f || ry <= 0f)
                    continue;

                float invRx = 1f / rx;
                float invRy = 1f / ry;

                float areaY = (h - 1) - area.Y;
                float strongMul = area.Strong * area.BumpBooster;

                Parallel.For(0, h, y =>
                {
                    float dy = y - areaY;

                    for (int x = 0; x < w; x++)
                    {
                        int idx = y * w + x;

                        float dx = x - area.X;

                        float nx = dx * invRx;
                        float ny = dy * invRy;

                        float ellipseVal = nx * nx + ny * ny;
                        if (ellipseVal > 1f)
                            continue;

                        float t = Mathf.Sqrt(ellipseVal);
                        float mask = 1f - t * t * t;

                        float weightB = mask;
                        float weightA = 1f - weightB;

                        Color a = cur[idx];
                        Color b = B[idx];

                        float gMerged = a.g * weightA + b.g * weightB;
                        float bMerged = a.b * weightA + b.b * weightB;
                        float aMerged = a.a * weightA + b.a * weightB;

                        float factor = 1f + mask * strongMul;

                        gMerged = 0.74f + (gMerged - 0.74f) * factor;
                        bMerged = 0.74f + (bMerged - 0.74f) * factor;
                        aMerged = 0.50f + (aMerged - 0.50f) * factor;

                        cur[idx] = new Color(
                            1f,
                            Mathf.Clamp01(gMerged),
                            Mathf.Clamp01(bMerged),
                            Mathf.Clamp01(aMerged)
                        );
                    }
                });
            }

            // ⬇⬇ 이 부분이 원래 안 되는 케이스였음 (범위 지정 필요)
            rgbA.SetPixels(0, 0, w, h, cur);
            rgbA.Apply();
            return rgbA;
        }

        // private static Texture2D MergeRGBAlphaMaps(Texture2D rgbA, Texture2D rgbB, List<BArea> areas = null)
        // {
        //     int w = Mathf.Min(rgbA.width, rgbB.width);
        //     int h = Mathf.Min(rgbA.height, rgbB.height);
        //     bool useArea = (areas != null && areas.Count > 0);

        //     // 기본 A 복사
        //     Color[] cur = rgbA.GetPixels(0, 0, w, h);
        //     Color[] B = rgbB.GetPixels(0, 0, w, h);

        //     if (!useArea)
        //     {
        //         Texture2D onlyA = new Texture2D(w, h, TextureFormat.RGBA32, false);
        //         onlyA.SetPixels(cur);
        //         onlyA.Apply();
        //         return onlyA;
        //     }

        //     // 🔥 순차적 오버레이 적용
        //     foreach (var area in areas)
        //     {
        //         Parallel.For(0, h, y =>
        //         {
        //             float areaY = (h - 1) - area.Y;

        //             for (int x = 0; x < w; x++)
        //             {
        //                 int idx = y * w + x;

        //                 float dx = x - area.X;
        //                 float dy = y - areaY;

        //                 float rx = area.RadiusX > 0 ? area.RadiusX : area.RadiusY;
        //                 float ry = area.RadiusY > 0 ? area.RadiusY : area.RadiusX;
        //                 if (rx <= 0f || ry <= 0f) continue;

        //                 float nx = dx / rx;
        //                 float ny = dy / ry;
        //                 float ellipseVal = nx * nx + ny * ny;
        //                 if (ellipseVal > 1f) continue; // 바깥이면 skip

        //                 // mask: 1 - t^3
        //                 float t = Mathf.Clamp01(Mathf.Sqrt(ellipseVal));
        //                 float mask = 1f - Mathf.Pow(t, 3f);

        //                 // blending weight
        //                 float weightB = mask;
        //                 float weightA = 1f - weightB;

        //                 Color a = cur[idx];
        //                 Color b = B[idx];

        //                 float gMerged = a.g * weightA + b.g * weightB;
        //                 float bMerged = a.b * weightA + b.b * weightB;
        //                 float aMerged = a.a * weightA + b.a * weightB;

        //                 // Booster / Strong 적용
        //                 float factor = 1f + mask * area.Strong * area.BumpBooster;

        //                 float baseG = 0.74f;
        //                 float baseB = 0.74f;
        //                 float baseA = 0.5f;

        //                 gMerged = baseG + (gMerged - baseG) * factor;
        //                 bMerged = baseB + (bMerged - baseB) * factor;
        //                 aMerged = baseA + (aMerged - baseA) * factor;

        //                 cur[idx] = new Color(
        //                     1f,
        //                     Mathf.Clamp01(gMerged),
        //                     Mathf.Clamp01(bMerged),
        //                     Mathf.Clamp01(aMerged)
        //                 );
        //             }
        //         });
        //     }

        //     // 최종 Texture 생성
        //     Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        //     result.SetPixels(cur);
        //     result.Apply();
        //     return result;
        // }


       private static Texture2D BlendTexture(Texture2D src, Texture2D dst, int centerX, int centerY, int radius, float weight)
        {
            // ---------------------------
            // 1) GPU용 RenderTexture 준비
            // ---------------------------
            RenderTexture srcRT = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            srcRT.enableRandomWrite = true;
            Graphics.Blit(src, srcRT);

            RenderTexture dstRT = new RenderTexture(dst.width, dst.height, 0, RenderTextureFormat.ARGB32);
            dstRT.enableRandomWrite = true;
            Graphics.Blit(dst, dstRT);

            // ---------------------------
            // 2) ComputeShader 로드 및 커널
            // ---------------------------
            ComputeShader cs = _self._bundle.LoadAsset<ComputeShader>("BlendBumpmap");
            int kernel = cs.FindKernel("CSMain");

            // ---------------------------
            // 3) Blend 영역 계산
            // ---------------------------
            int rectX = centerX - radius;
            int rectY = (dst.height - centerY - 1) - radius; // GPU y좌표(top origin)
            int rectW = radius * 2;
            int rectH = radius * 2;

            if (rectX < 0) { rectW += rectX; rectX = 0; }
            if (rectY < 0) { rectH += rectY; rectY = 0; }
            rectW = Mathf.Min(rectW, dst.width - rectX);
            rectH = Mathf.Min(rectH, dst.height - rectY);

            // ---------------------------
            // 4) ComputeShader 파라미터 전달
            // ---------------------------
            cs.SetInt("srcWidth", src.width);
            cs.SetInt("srcHeight", src.height);
            cs.SetInt("dstWidth", dst.width);
            cs.SetInt("dstHeight", dst.height);

            cs.SetInt("rectX", rectX);
            cs.SetInt("rectY", rectY);
            cs.SetInt("rectW", rectW);
            cs.SetInt("rectH", rectH);

            cs.SetFloat("radius", radius);
            cs.SetFloat("weight", weight);

            cs.SetTexture(kernel, "SrcTex", srcRT);
            cs.SetTexture(kernel, "DstTex", dstRT);

            // ---------------------------
            // 5) Dispatch
            // ---------------------------
            int threadX = Mathf.CeilToInt(rectW / 8f);
            int threadY = Mathf.CeilToInt(rectH / 8f);
            cs.Dispatch(kernel, threadX, threadY, 1);

            // ---------------------------
            // 6) GPU 결과 → CPU Texture2D
            // ---------------------------
            Texture2D resultTex = new Texture2D(dst.width, dst.height, TextureFormat.RGBA32, false);

            RenderTexture.active = dstRT;
            resultTex.ReadPixels(new Rect(0, 0, dst.width, dst.height), 0, 0);
            resultTex.Apply();
            RenderTexture.active = null;

            // ---------------------------
            // 7) RenderTexture 해제
            // ---------------------------
            srcRT.Release();
            dstRT.Release();

            return resultTex;
        }
        

        private static Vector3 GetBoneRotationFromTF(Transform info)
        {
            Quaternion rot = info.rotation;
            Vector3 euler = rot.eulerAngles;

            // 필요하면 -180~180 보정
            if (euler.x > 180f) euler.x -= 360f;
            if (euler.y > 180f) euler.y -= 360f;
            if (euler.z > 180f) euler.z -= 360f;

            return euler;
        }

        private static Vector3 GetBoneRotationFromIK(OCIChar.IKInfo info)
        {
            Quaternion rot = info.guideObject.transformTarget.localRotation;
            Vector3 euler = rot.eulerAngles;

            // 필요하면 -180~180 보정
            if (euler.x > 180f) euler.x -= 360f;
            if (euler.y > 180f) euler.y -= 360f;
            if (euler.z > 180f) euler.z -= 360f;

            return euler;
        }

        private static Vector3 GetBoneRotationFromFK(OCIChar.BoneInfo info)
        {

            Vector3 euler = info.guideObject.changeAmount.rot;

            // 필요하면 -180~180 보정
            if (euler.x > 180f) euler.x -= 360f;
            if (euler.y > 180f) euler.y -= 360f;
            if (euler.z > 180f) euler.z -= 360f;

            return euler;
        }

        private static Texture2D ConvertToTexture2D(RenderTexture renderTex)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTex;

            Texture2D tex = new Texture2D(renderTex.width, renderTex.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, renderTex.width, renderTex.height), 0, 0);
            tex.Apply();

            RenderTexture.active = previous;

            return tex;
        }

        private static RealHumanData AllocateBumpMap(OCIChar ociChar, RealHumanData realHumanData)
        {
            Texture2D headOriginTexture = null;
            Texture2D bodyOriginTexture = null;
   
            if (realHumanData.cf_m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as Texture2D == null)
                headOriginTexture = ConvertToTexture2D(realHumanData.cf_m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as RenderTexture);
            else 
                headOriginTexture = realHumanData.cf_m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as Texture2D;

            if (realHumanData.cf_m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as Texture2D == null)
                bodyOriginTexture = ConvertToTexture2D(realHumanData.cf_m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as RenderTexture);
            else
                bodyOriginTexture = realHumanData.cf_m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as Texture2D;

            // realHumanData.body_bumpScale2 = realHumanData.cf_m_skin_body.GetFloat("_BumpScale2");

            realHumanData.headOriginTexture = SetTextureSize(MakeReadableTexture(headOriginTexture), _self._faceExpressionFemaleBumpMap2.width, _self._faceExpressionFemaleBumpMap2.height);
            realHumanData.bodyOriginTexture = SetTextureSize(MakeReadableTexture(bodyOriginTexture), _self._bodyStrongFemaleBumpMap2.width, _self._bodyStrongFemaleBumpMap2.height);
    
            return realHumanData;
        }

        private static RealHumanData InitRealHumanData(OCIChar ociChar, RealHumanData realHumanData)
        {
            realHumanData.c_m_eye.Clear();
            SkinnedMeshRenderer[] sks = ociChar.guideObject.transformTarget.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (SkinnedMeshRenderer render in sks.ToList())
            {
                foreach (var mat in render.sharedMaterials)
                {
                    string name = mat.name.ToLower();
                    if (name.Contains("cf_m_skin_body"))
                    {
                        // if (mat.shader.name.Contains("Hanmen/Next-Gen Body"))
                        // {
                        //     realHumanData.bodyShaderType = BODY_SHADER.HANMAN;
                        // } 
                        // else
                        // {
                        //     realHumanData.bodyShaderType = BODY_SHADER.DEFAULT;
                        // }

                        realHumanData.cf_m_skin_body = render.material;
                    }
                    else if (name.Contains("cf_m_skin_head"))
                    {
                        // if (mat.shader.name.Contains("Hanmen/Next-Gen Face"))
                        // {
                        //     realHumanData.faceShaderType = BODY_SHADER.HANMAN;
                        // } 
                        // else
                        // {
                        //     realHumanData.faceShaderType = BODY_SHADER.DEFAULT;
                        // }

                        realHumanData.cf_m_skin_head = render.material;
                    }
                    else if (name.Contains("c_m_eye"))
                    {
                        realHumanData.c_m_eye.Add(render.material);
                    }
                }
            }
                    
            if (realHumanData.cf_m_skin_body.GetTexture("_BumpMap2") != null)
            {
                realHumanData.body_bumpmap_name = "_BumpMap2";
            } 
            else if (realHumanData.cf_m_skin_body.GetTexture("_BumpMap") != null)
            {
                realHumanData.body_bumpmap_name = "_BumpMap";
            }
            else
            {
                realHumanData.body_bumpmap_name = "";
            }
            
            if (realHumanData.cf_m_skin_head.GetTexture("_BumpMap2") != null)
            {
                realHumanData.head_bumpmap_name = "_BumpMap2";
            }
            else if (realHumanData.cf_m_skin_head.GetTexture("_BumpMap") != null)
            {
                realHumanData.head_bumpmap_name = "_BumpMap";
            }
            else
            {
                realHumanData.head_bumpmap_name = "";
            }


            if (!realHumanData.body_bumpmap_name.Contains("_BumpMap2"))
                return null;
            else
            {
                realHumanData = AllocateBumpMap(ociChar, realHumanData);

                // realHumanData.tf_j_l_foot = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Foot01_L");
                // realHumanData.tf_j_r_foot = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Foot01_R");

                foreach (OCIChar.BoneInfo bone in ociChar.listBones)
                {
                    if (bone.guideObject != null && bone.guideObject.transformTarget != null) {
                        // UnityEngine.Debug.Log($">> bone : + {bone.guideObject.transformTarget.name}");

                        if(bone.guideObject.transformTarget.name.Contains("cf_J_Spine01"))
                        {
                            realHumanData.fk_spine01_bone = bone; // 하단
                        }
                        else if(bone.guideObject.transformTarget.name.Contains("cf_J_Spine02"))
                        {
                            realHumanData.fk_spine02_bone = bone; // 상단
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_ArmUp00_L"))
                        {
                            realHumanData.fk_left_shoudler_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_ArmUp00_R"))
                        {
                            realHumanData.fk_right_shoudler_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_LegUp00_L"))
                        {
                            realHumanData.fk_left_thigh_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_LegUp00_R"))
                        {
                            realHumanData.fk_right_thigh_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_LegLow01_L"))
                        {
                            realHumanData.fk_left_knee_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_LegLow01_R"))
                        {
                            realHumanData.fk_right_knee_bone = bone;
                        }                        
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_Hand_L"))
                        {
                            realHumanData.fk_left_hand_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_Hand_R"))
                        {
                            realHumanData.fk_right_hand_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_Neck"))
                        {
                            realHumanData.fk_neck_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_Foot01_L"))
                        {
                            realHumanData.fk_left_foot_bone = bone;
                        }   
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_Foot01_R"))
                        {
                            realHumanData.fk_right_foot_bone = bone;
                        }                 
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_Toes01_L"))
                        {
                            realHumanData.fk_left_toes_bone = bone;
                        }   
                        else if (bone.guideObject.transformTarget.name.Contains("cf_J_Toes01_R"))
                        {
                            realHumanData.fk_right_toes_bone = bone;
                        } 
                        
                    }
                }

                realHumanData.lk_left_foot_bone = ociChar.listIKTarget[9]; // left foot
                realHumanData.lk_right_foot_bone = ociChar.listIKTarget[12]; // right foot
                
            }

            return realHumanData;
        }

        private static float Remap(float value, float inMin, float inMax, float outMin, float outMax)
        {
            return outMin + (value - inMin) * (outMax - outMin) / (inMax - inMin);
        }

  
        private static void DoFaceRealEffect(OCIChar ociChar, RealHumanData realHumanData)
        {
            if (realHumanData.cf_m_skin_head != null)
            {
                // UnityEngine.Debug.Log($">> DoFaceRealEffect");
                if (FaceBumpActive.Value)
                {
                    Texture2D origin_texture = realHumanData.headOriginTexture;
                    Texture2D express_texture = null;

                    if (ociChar.charInfo.sex == 1) // female
                    {
                        express_texture = _self._faceExpressionFemaleBumpMap2;
                    }
                    else
                    {
                        express_texture = _self._faceExpressionMaleBumpMap2;
                    }

                    // test
                    List<BArea> areas = new List<BArea>();

                    if (AddCheekbone.Value)
                    {  
                        areas.Add(new BArea(330, 600, 115, 115, 0.7f));
                        areas.Add(new BArea(700, 600, 115, 115, 0.7f));

                    }

                    RealFaceData realFaceData = null;

                    if (_self._faceMouthMgmt.TryGetValue(_self._mouth_type, out realFaceData))
                    {
                        foreach(BArea area in realFaceData.areas)
                        {
                            areas.Add(area);
                        }
                    }

                    if (_self._faceEyesMgmt.TryGetValue(_self._eye_type, out realFaceData))
                    {
                        foreach(BArea area in realFaceData.areas)
                        {
                            areas.Add(area);
                        }
                    }

                    if (origin_texture != null)
                    {
                        UnityEngine.Debug.Log($">> _mouth_type {_self._mouth_type}. _faceMouthMgmt {_self._faceMouthMgmt.Count}, areas.Count {areas.Count}");
                        Texture2D merged =  MergeRGBAlphaMaps(origin_texture, express_texture, areas);
                        realHumanData.cf_m_skin_head.SetTexture(realHumanData.head_bumpmap_name, merged);
                        // SaveAsPNG(merged, "./face_merged.png");
                    }
                } 
                else
                {
                    realHumanData.cf_m_skin_head.SetTexture(realHumanData.head_bumpmap_name, realHumanData.headOriginTexture);
                }
            }
        }

        private static void DoBodyRealEffect(OCIChar ociChar, RealHumanData realHumanData)
        {
            // UnityEngine.Debug.Log($">> DoBodyRealEffect");

            if (realHumanData.cf_m_skin_head == null || realHumanData.cf_m_skin_body == null)
                return; 
            
            if (BodyBumpActive.Value)
            {   
                Texture2D origin_texture = realHumanData.bodyOriginTexture;
                Texture2D strong_texture = null;

                if (ociChar.charInfo.sex == 1) // female
                {
                    strong_texture = _self._bodyStrongFemaleBumpMap2;
                }
                else
                {
                    strong_texture = _self._bodyStrongMaleBumpMap2;
                }

                List<BArea> areas = new List<BArea>();
                // Vector3 l_foot_tf_rot = GetBoneRotationFromTF(realHumanData.tf_j_l_foot);
                // Vector3 r_foot_tf_rot = GetBoneRotationFromTF(realHumanData.tf_j_r_foot);
                realHumanData.prev_lk_left_foot_rot = GetBoneRotationFromIK(realHumanData.lk_left_foot_bone);
                realHumanData.prev_lk_right_foot_rot = GetBoneRotationFromIK(realHumanData.lk_right_foot_bone);                
                realHumanData.prev_fk_left_foot_rot = GetBoneRotationFromFK(realHumanData.fk_left_foot_bone);
                realHumanData.prev_fk_right_foot_rot = GetBoneRotationFromFK(realHumanData.fk_right_foot_bone);
                realHumanData.prev_fk_left_thigh_rot = GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone);                                
                realHumanData.prev_fk_right_thigh_rot = GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone);
                realHumanData.prev_fk_left_knee_rot = GetBoneRotationFromFK(realHumanData.fk_left_knee_bone);                                
                realHumanData.prev_fk_right_knee_rot = GetBoneRotationFromFK(realHumanData.fk_right_knee_bone);                
                realHumanData.prev_fk_left_shoudler_rot = GetBoneRotationFromFK(realHumanData.fk_left_shoudler_bone);
                realHumanData.prev_fk_right_shoudler_rot = GetBoneRotationFromFK(realHumanData.fk_right_shoudler_bone);
                realHumanData.prev_fk_neck_rot = GetBoneRotationFromFK(realHumanData.fk_neck_bone);
                realHumanData.prev_fk_spine01_rot = GetBoneRotationFromFK(realHumanData.fk_spine01_bone);
                realHumanData.prev_fk_spine02_rot = GetBoneRotationFromFK(realHumanData.fk_spine02_bone);
                

                float bumpscale = 0.0f;
                // if (l_foot_rot.x > 5.0f || l_foot_tf_rot.x > 5.0f)
               // if (ociChar.fkCtrl.enabled)

                // UnityEngine.Debug.Log($">> ociChar.oiCharInfo.enableFK {ociChar.oiCharInfo.enableFK}, ociChar.oiCharInfo.enable.IK {ociChar.oiCharInfo.enableIK}");

                if(ociChar.oiCharInfo.enableIK) {
                    if (realHumanData.prev_lk_left_foot_rot.x > 5.0f)
                    {   
                        bumpscale = Math.Min(Remap(Math.Max(realHumanData.prev_lk_left_foot_rot.x, 0), 5.0f, 120.0f, 0.1f, 2.5f), 2.5f);
                        areas.Add(new BArea(400, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                        areas.Add(new BArea(660, 1200, 160, 360, bumpscale)); // 뒷쪽 허벅지
                        areas.Add(new BArea(620, 720, 160, 160, bumpscale)); // 엉덩이

                        areas.Add(new BArea(660, 1465, 120, 160, bumpscale * 1.3f)); // 뒷쪽 종아리 강조
                        // UnityEngine.Debug.Log($">> l_foot_rot {l_foot_rot.x}, l_foot_tf_rot {l_foot_tf_rot.x} bumpscale {bumpscale}");
                    } 


                    if (realHumanData.prev_lk_right_foot_rot.x > 5.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Max(realHumanData.prev_lk_right_foot_rot.x, 0), 5.0f, 90.0f, 0.1f, 2f), 2f);
                        areas.Add(new BArea(110, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                        areas.Add(new BArea(910, 1200, 160, 360, bumpscale)); // 뒷쪽 허벅지
                        areas.Add(new BArea(930, 720, 160, 160, bumpscale)); // 엉덩이

                        areas.Add(new BArea(885, 1465, 120, 160, bumpscale * 1.5f)); // 뒷쪽 종아리 강조
                        // UnityEngine.Debug.Log($">> r_foot_rot {r_foot_rot}, bumpscale {bumpscale}");
                    } 
                }

                if (ociChar.oiCharInfo.enableFK)
                {
                    // UnityEngine.Debug.Log($">> spine01_rot {realHumanData.prev_fk_spine01_rot}, spine02_rot {realHumanData.prev_fk_spine02_rot}");
                    // UnityEngine.Debug.Log($">> l_thight_rot {realHumanData.prev_fk_left_thigh_rot}, r_tight_rot {realHumanData.prev_fk_right_thigh_rot}");
                    
                    // 허벅지
                    if (realHumanData.prev_fk_left_thigh_rot.x > 5.0f) // 뒷 방향
                    {   
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_left_thigh_rot.x, 5.0f, 90.0f, 0.1f, 1.5f), 1.5f);
                        areas.Add(new BArea(660, 1200, 160, 360, bumpscale)); // 뒷쪽 허벅지
                        areas.Add(new BArea(620, 720, 160, 160, bumpscale * 1.8f)); // 엉덩이;                      
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_left_thigh_rot.x) // 앞방향
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_left_thigh_rot.x), 10.0f, 90.0f, 0.1f, 1.5f), 1.5f);
                        areas.Add(new BArea(400, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                    }

                    if (realHumanData.prev_fk_right_thigh_rot.x > 5.0f) // 뒷 방향
                    {
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_right_thigh_rot.x, 5.0f, 90.0f, 0.1f, 1.5f), 1.5f);
                        areas.Add(new BArea(910, 1200, 160, 360, bumpscale)); // 뒷쪽 허벅지
                        areas.Add(new BArea(930, 720, 160, 160, bumpscale * 1.8f)); // 엉덩이
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_right_thigh_rot.x && realHumanData.prev_fk_right_thigh_rot.x < -10.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_right_thigh_rot.x), 10.0f, 90.0f, 0.1f, 1.5f), 1.5f);
                        areas.Add(new BArea(110, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                    }

                    // 무릎
                    if (realHumanData.prev_fk_left_knee_rot.x > 5.0f) // 뒷 방향
                    {   
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_left_knee_rot.x, 5.0f, 90.0f, 0.1f, 2f), 2f);
                        areas.Add(new BArea(660, 1200, 160, 360, bumpscale)); // 뒷쪽 허벅지                      
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_left_knee_rot.x && realHumanData.prev_fk_left_knee_rot.x < -10.0f) // 앞방향
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_left_knee_rot.x), 10.0f, 90.0f, 0.1f, 2f), 2f);
                        areas.Add(new BArea(400, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                    }

                    if (realHumanData.prev_fk_right_knee_rot.x > 5.0f) // 뒷 방향
                    {
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_right_knee_rot.x, 5.0f, 90.0f, 0.1f, 2f), 2f);
                        areas.Add(new BArea(910, 1200, 160, 360, bumpscale)); // 뒷쪽 허벅지                
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_right_knee_rot.x && realHumanData.prev_fk_right_knee_rot.x < -10.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_right_knee_rot.x), 10.0f, 90.0f, 0.1f, 2f), 2f);
                        areas.Add(new BArea(110, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                    }

                    // 발목
                    if (realHumanData.prev_fk_left_foot_rot.x > 5.0f) // 뒤방향
                    {   
                        bumpscale = Math.Min(Remap(Math.Max(realHumanData.prev_fk_left_foot_rot.x, 0), 5.0f, 90.0f, 0.1f, 2f), 2f);
                        areas.Add(new BArea(400, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                        areas.Add(new BArea(660, 1200, 160, 360, bumpscale * 0.7f)); // 뒷쪽 허벅지
                        areas.Add(new BArea(620, 720, 160, 160, bumpscale * 0.5f)); // 엉덩이

                        areas.Add(new BArea(660, 1465, 120, 160, bumpscale * 1.5f)); // 뒷쪽 종아리 강조
                    } 
                   
                    if (realHumanData.prev_fk_right_foot_rot.x > 5.0f) // 뒤방향
                    {
                        bumpscale = Math.Min(Remap(Math.Max(realHumanData.prev_fk_right_foot_rot.x, 0), 5.0f, 90.0f, 0.1f, 2f), 2f);
                        areas.Add(new BArea(110, 1200, 160, 600, bumpscale)); // 앞 허벅지/정강이
                        areas.Add(new BArea(910, 1200, 160, 360, bumpscale * 0.7f)); // 뒷쪽 허벅지
                        areas.Add(new BArea(930, 720, 160, 160, bumpscale * 0.5f)); // 엉덩이

                        areas.Add(new BArea(885, 1465, 120, 160, bumpscale * 1.5f)); // 뒷쪽 종아리 강조
                    } 

                    // 어깨
                    if (-90.0f <= realHumanData.prev_fk_left_shoudler_rot.z && realHumanData.prev_fk_left_shoudler_rot.x < -10.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_left_shoudler_rot.z), 10.0f, 90.0f, 0.1f, 0.5f), 0.5f);
                        areas.Add(new BArea(490, 225, 120, 120, bumpscale));
                        areas.Add(new BArea(1050, 2000, 120, 120, bumpscale));
                    } 

                    if (-90.0f <= realHumanData.prev_fk_right_shoudler_rot.z && realHumanData.prev_fk_right_shoudler_rot.x < -10.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_right_shoudler_rot.z), 10.0f, 90.0f, 0.1f, 0.5f), 0.5f);
                        areas.Add(new BArea(35, 225, 120, 120, bumpscale));
                        areas.Add(new BArea(2030, 1670, 60, 60, bumpscale));
                    } 

                    // 목
                    if (realHumanData.prev_fk_neck_rot.x > 5.0f) // 뒷쪽
                    {
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_neck_rot.x, 5.0f, 90.0f, 0.5f, 2.5f), 2.5f);
                        areas.Add(new BArea(780, 230, 60, 120, bumpscale)); // 척추 위쪽
                        // UnityEngine.Debug.Log($">> neck_rot {neck_rot}, bumpscale {bumpscale}");
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_neck_rot.x && realHumanData.prev_fk_neck_rot.x < -5.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_neck_rot.x), 5.0f, 90.0f, 0.5f, 2.5f), 2.5f);
                        areas.Add(new BArea(250, 160, 240, 140, bumpscale)); // 쇠골
                        // UnityEngine.Debug.Log($">> neck_rot {neck_rot}, bumpscale {bumpscale}");
                    }
                    
                    // 허리
                    // 갈비뼈 왼쪽/오른쪽
                    if (realHumanData.prev_fk_spine01_rot.y > 1.0f) // 갈비뼈 오른쪽
                    {
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_spine01_rot.x, 1.0f, 90.0f, 0.1f, 2.2f), 2.2f);
                        areas.Add(new BArea(420, 505, 160, 220, bumpscale));
                        // UnityEngine.Debug.Log($">> spine1_rot {spine1_rot}, bumpscale {bumpscale}");
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_spine01_rot.y && realHumanData.prev_fk_spine01_rot.y <= -5.0f) // 갈비뼈 왼쪽
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_spine01_rot.x), 1.0f, 90.0f, 0.1f, 2.2f), 2.2f);
                        areas.Add(new BArea(100, 505, 160, 220, bumpscale));
                        // UnityEngine.Debug.Log($">> spine1_rot {spine1_rot}, bumpscale {bumpscale}");
                    }

                    if (realHumanData.prev_fk_spine01_rot.x > 1.0f) // 뒷쪽
                    {
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_spine01_rot.x, 1.0f, 90.0f, 0.1f, 2.0f), 2.0f);
                        areas.Add(new BArea(780, 410, 60, 400, bumpscale)); // 척추
                        // UnityEngine.Debug.Log($">> spine2_rot {spine2_rot}, bumpscale {bumpscale}");
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_spine01_rot.x && realHumanData.prev_fk_spine01_rot.x <= -1.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_spine01_rot.x), 1.0f, 90.0f, 0.1f, 2.0f), 2.0f);
                        areas.Add(new BArea(255, 530, 500, 180, bumpscale)); // 갈비뼈
                        // UnityEngine.Debug.Log($">> spine2_rot {realHumanData.prev_fk_spine02_rot}, bumpscale {bumpscale}");
                    }     

                    if (realHumanData.prev_fk_spine02_rot.x > 1.0f) // 뒷쪽
                    {
                        bumpscale = Math.Min(Remap(realHumanData.prev_fk_spine02_rot.x, 1.0f, 90.0f, 0.1f, 2.0f), 2.0f);
                        areas.Add(new BArea(780, 410, 60, 400, bumpscale)); // 척추
                        // UnityEngine.Debug.Log($">> spine2_rot {spine2_rot}, bumpscale {bumpscale}");
                    } 
                    else if (-90.0f <= realHumanData.prev_fk_spine02_rot.x && realHumanData.prev_fk_spine02_rot.x <= -1.0f)
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(realHumanData.prev_fk_spine02_rot.x), 1.0f, 90.0f, 0.1f, 2.0f), 2.0f);
                        areas.Add(new BArea(255, 530, 500, 180, bumpscale)); // 갈비뼈
                        UnityEngine.Debug.Log($">> spine2_rot {realHumanData.prev_fk_spine02_rot}, bumpscale {bumpscale}");
                        // UnityEngine.Debug.Log($">> spine2_rot {realHumanData.prev_fk_spine02_rot}, bumpscale {bumpscale}");
                    }               
                }            

                if (origin_texture != null)
                {
                    Texture2D merged =  MergeRGBAlphaMaps(origin_texture, strong_texture, areas);
                    realHumanData.cf_m_skin_body.SetTexture(realHumanData.body_bumpmap_name, merged);
                    // SaveAsPNG(merged, "./body_merged.png");
                }
            } 
            else
            {
                realHumanData.cf_m_skin_body.SetTexture(realHumanData.body_bumpmap_name, realHumanData.bodyOriginTexture);
            }
        }
        
        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                _self._selectedOciChar = objectCtrlInfo as OCIChar;

                foreach(ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    if (ctrlInfo != objectCtrlInfo)
                    {
                        OCIChar ociChar = ctrlInfo as OCIChar;
                        if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                        {
                            if (realHumanData.coroutine != null)
                            {
                                ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                            }
                            _self._ociCharMgmt.Remove(ociChar.GetHashCode());
                        }
                    }
                }

                _self._selectedOCIs.Clear();
                _self._selectedOCIs.Add(objectCtrlInfo);     


                OCIChar ociChar2 = objectCtrlInfo as OCIChar;

                if (_self._ociCharMgmt.TryGetValue(ociChar2.GetHashCode(), out var realHumanData1))
                {
                    ociChar2.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar2, realHumanData1));
                }
                else
                {
                    RealHumanData realHumanData2 = new RealHumanData();
                    realHumanData2 = InitRealHumanData(ociChar2, realHumanData2);

                    if (realHumanData2 != null)
                    {
                        realHumanData2.coroutine = ociChar2.charInfo.StartCoroutine(_self.Routine(realHumanData2));                    
                        _self._ociCharMgmt.Add(ociChar2.GetHashCode(), realHumanData2);
                        ociChar2.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar2, realHumanData2));
                    } else
                    {
                        Logger.LogMessage($"Body skin not has bumpmap2");
                    }
                }                

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {

                foreach (TreeNodeObject node in Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes)
                {
                    ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(node);
                    OCIChar ociChar = objectCtrlInfo as OCIChar;                    
                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
                    }
                    else
                    {
                        // RealHumanData realHumanData2 = new RealHumanData();
                        // realHumanData2.coroutine = ociChar.charInfo.StartCoroutine(_self.Routine(realHumanData2));
                        // _self._ociCharMgmt.Add(ociChar.GetHashCode(), realHumanData2);
                        // ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData2));
                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2 = InitRealHumanData(ociChar, realHumanData2);

                        if (realHumanData2 != null)
                        {
                            realHumanData2.coroutine = ociChar.charInfo.StartCoroutine(_self.Routine(realHumanData2));                    
                            _self._ociCharMgmt.Add(ociChar.GetHashCode(), realHumanData2);
                            ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData2));
                        } else
                        {
                            Logger.LogMessage($"Body skin not has bumpmap2");
                        }                        
                    }                    
                }
                
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {

                foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        if (realHumanData.coroutine != null)
                        {
                            ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                        }
                        realHumanData.c_m_eye.Clear();
                        _self._ociCharMgmt.Remove(ociChar.GetHashCode());
                    }
                }

                _self._selectedOciChar = null;
                _self._selectedOCIs.Clear();               

                return true;
            }
        }


        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {        

                foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        if (realHumanData.coroutine != null)
                        {
                            ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                        }
                        realHumanData.c_m_eye.Clear();

                        _self._ociCharMgmt.Remove(ociChar.GetHashCode());
                    }
                }

                _self._selectedOCIs.Clear();  

                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                ChaControl chaControl = __instance.GetChaControl();

                UnityEngine.Debug.Log($">> ChangeChara");

                if (chaControl != null)
                {                    
                     if (_self._ociCharMgmt.TryGetValue(__instance.GetHashCode(), out var realHumanData))
                    {
                        _self._ociCharMgmt.Remove(__instance.GetHashCode());
                        if (realHumanData.coroutine != null)
                        {
                            __instance.charInfo.StopCoroutine(realHumanData.coroutine);
                        }
                        realHumanData.c_m_eye.Clear();                      
                    }

                    // RealHumanData realHumanData2 = new RealHumanData();

                    // realHumanData2.coroutine = __instance.charInfo.StartCoroutine(_self.Routine(realHumanData));
                    // _self._ociCharMgmt.Add(__instance.GetHashCode(), realHumanData2);
                    // __instance.charInfo.StartCoroutine(ExecuteAfterFrame(__instance, realHumanData2));
                    RealHumanData realHumanData2 = new RealHumanData();
                    realHumanData2 = InitRealHumanData(__instance, realHumanData2);

                    if (realHumanData2 != null)
                    {
                        realHumanData2.coroutine = __instance.charInfo.StartCoroutine(_self.Routine(realHumanData2));                    
                        _self._ociCharMgmt.Add(__instance.GetHashCode(), realHumanData2);
                        __instance.charInfo.StartCoroutine(ExecuteAfterFrame(__instance, realHumanData2));
                    } else
                    {
                        Logger.LogMessage($"Body skin not has bumpmap2");
                    }                    
                }
            }
        }

        // 표정 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeMouthPtn", typeof(int), typeof(bool))]
        private static class ChaControl_ChangeMouthPtn_Patches
        {
            private static void Postfix(ChaControl __instance, int ptn, bool blend)
            {
                // UnityEngine.Debug.Log($">> ChangeMouthPtn {ptn}");
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                if (ociChar != null)
                {
                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        _self._mouth_type = ptn;
                        DoFaceRealEffect(ociChar, realHumanData);
                    }
                }
            }
        }

        // 표정 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeEyesPtn", typeof(int), typeof(bool))]
        private static class ChaControl_ChangeEyesPtn_Patches
        {
            private static void Postfix(ChaControl __instance, int ptn, bool blend)
            {
                // UnityEngine.Debug.Log($">> ChangeEyesPtn {ptn}");

                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
                if (ociChar != null)
                {
                    if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                    {
                        _self._eye_type = ptn;
                        DoFaceRealEffect(ociChar, realHumanData);
                    }
                }
            }
        }


        // // 악세러리 부분 변경
        // [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        // private static class ChaControl_ChangeAccessory_Patches
        // {
        //     private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
        //     {
        //         OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //         if (ociChar != null)
        //         {
        //             if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
        //             {
        //                 ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
        //             }
        //         }
        //     }
        // }

        // // 옷 부분 변경
        // [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        // private static class ChaControl_ChangeClothes_Patches
        // {
        //     private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
        //     {
        //         OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //         if (ociChar != null)
        //         {
        //             if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
        //             {
        //                 ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
        //             }
        //         }
        //     }
        // }

        // // 옷 전체 변경
        // [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        // internal static class ChaControl_SetAccessoryStateAll_Patches
        // {
        //     public static void Postfix(ChaControl __instance, bool show)
        //     {
        //         OCIChar ociChar = __instance.GetOCIChar() as OCIChar;
        //         if (ociChar != null)
        //         {
        //             if (_self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
        //             {
        //                 ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
        //             }
        //         }
        //     }
        // }
        
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

    class BArea
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float RadiusX; // 가로 반지름
        public float RadiusY; // 세로 반지름
        public float Strong = 1.0f;
        public float BumpBooster = 1.0f;

        public BArea(float x, float y, float radiusX, float radiusY)
        {
            X = x;
            Y = y;
            RadiusX = radiusX;
            RadiusY = radiusY;
        }

        public BArea(float x, float y, float radiusX, float radiusY, float bumpBooster)
        {
            X = x;
            Y = y;
            RadiusX = radiusX;
            RadiusY = radiusY;
            BumpBooster = bumpBooster;
        }
    }

    class RealFaceData
    {
        public List<BArea> areas = new List<BArea>();

        public RealFaceData()
        {
        }
        public RealFaceData(BArea barea)
        {
           this.areas.Add(barea);
        }

        public RealFaceData(BArea barea1, BArea barea2)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
        }

        public RealFaceData(BArea barea1, BArea barea2, BArea barea3)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
            this.areas.Add(barea3);
        }

        public RealFaceData(BArea barea1, BArea barea2, BArea barea3, BArea barea4)
        {
            this.areas.Add(barea1);
            this.areas.Add(barea2);
            this.areas.Add(barea3);
            this.areas.Add(barea4);
        }
        public void Add(BArea area)
        {
            this.areas.Add(area);
        }
    }
    
    #endregion

    // enum BODY_SHADER
    // {
    //     HANMAN,
    //     DEFAULT
    // }

    class RealHumanData
    {
        public OCIChar   ociChar;
        public Coroutine coroutine;

        // public float  body_bumpScale2; 

        public string head_bumpmap_name;
        public string body_bumpmap_name;
        
        public DynamicBone_Ver02 rightBoob;
        public DynamicBone_Ver02 leftBoob;
        public DynamicBone_Ver02 rightButtCheek;
        public DynamicBone_Ver02 leftButtCheek;

        public Material cf_m_skin_head;
        public Material cf_m_skin_body;

        public List<Material> c_m_eye = new List<Material>();

        public Texture2D headOriginTexture;

        public Texture2D bodyOriginTexture;

        public OCIChar.BoneInfo fk_spine01_bone;

        public OCIChar.BoneInfo fk_spine02_bone;

        public OCIChar.BoneInfo fk_neck_bone;

        public OCIChar.BoneInfo fk_left_shoudler_bone;

        public OCIChar.BoneInfo fk_right_shoudler_bone;

        public OCIChar.BoneInfo fk_left_thigh_bone;

        public OCIChar.BoneInfo fk_right_thigh_bone;

        public OCIChar.BoneInfo fk_left_knee_bone;

        public OCIChar.BoneInfo fk_right_knee_bone;


        public OCIChar.BoneInfo fk_left_hand_bone;

        public OCIChar.BoneInfo fk_right_hand_bone;

        public OCIChar.BoneInfo  fk_right_foot_bone;

        public OCIChar.BoneInfo  fk_left_foot_bone;

        public OCIChar.BoneInfo  fk_right_toes_bone;

        public OCIChar.BoneInfo  fk_left_toes_bone;

        public OCIChar.IKInfo  lk_right_foot_bone;

        public OCIChar.IKInfo  lk_left_foot_bone;


        public Vector3 prev_fk_spine01_rot;
        public Vector3 prev_fk_spine02_rot;
        public Vector3 prev_fk_neck_rot;
        public Vector3 prev_fk_right_shoudler_rot;
        public Vector3 prev_fk_left_shoudler_rot;
        public Vector3 prev_fk_right_thigh_rot;
        public Vector3 prev_fk_left_thigh_rot;
        public Vector3 prev_fk_right_knee_rot;
        public Vector3 prev_fk_left_knee_rot;        
        public Vector3 prev_fk_right_foot_rot;
        public Vector3 prev_fk_left_foot_rot;        
        public Vector3 prev_lk_right_foot_rot;
        public Vector3 prev_lk_left_foot_rot;

        // public BODY_SHADER bodyShaderType = BODY_SHADER.DEFAULT;
        // public BODY_SHADER faceShaderType = BODY_SHADER.DEFAULT;

        // public Transform tf_j_l_foot; // 왼발 발목
        // public Transform tf_j_r_foot; // 오른발 발목

        // public Transform cm_j_l_arm_high; // 왼팔 상단
        // public Transform cm_j_r_arm_high; // 오른팔 상단

        // public Transform cm_j_l_leg_up; // 왼발 고관절
        // public Transform cm_j_r_leg_up; // 오른발 고관절

        // public Transform cm_j_l_leg_knee; // 왼발 무릎
        // public Transform cm_j_r_leg_knee; // 오른발 무릎

        // public Transform cm_j_l_leg_low; // 왼발 종아리
        // public Transform cm_j_r_leg_low; // 오른발 종아리


        // public Transform cm_j_l_toe; // 왼발 발가락
        // public Transform cm_j_r_toe; // 오른발 발가락

        // public Transform cm_j_neck; // 목

        // public Transform cm_j_hip; // hip

        // public Transform cm_j_spine_up; // 허리 하단
        // public Transform cm_j_spine_down; // 허리 하단
        
        public RealHumanData()
        {
            
        }        
    }

}