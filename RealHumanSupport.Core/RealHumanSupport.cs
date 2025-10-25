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


        internal static ConfigEntry<bool> EyeEarthQuakeActive { get; private set; }

        internal static ConfigEntry<bool> ExtraDynamicBoneColliderActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }

        internal static ConfigEntry<bool> FaceActive { get; private set; }


        // breath
        internal static ConfigEntry<float> BreathAmplitude { get; private set; }

        internal static ConfigEntry<float> BreathInterval { get; private set; }


        // end
        private Texture2D _faceDefaultBumpMap2;        

        private Texture2D _faceLaughBumpMap2;

        private Texture2D _facePukeBumpMap2;

        private Texture2D _faceWrinkleBumpMap2;

        private Texture2D _faceStrongSmileEffectBumpMap2;
            
        private Texture2D _eyeStrongBeautyEffectBumpMap2;


        private Texture2D _defaultBumpMap2;

        private Texture2D _laughBumpMap2;

        private Texture2D _pukeBumpMap2;



        private List<ObjectCtrlInfo> _selectedOCIs = new List<ObjectCtrlInfo>();

        private Dictionary<OCIChar, RealHumanData> _ociCharMgmt = new Dictionary<OCIChar, RealHumanData>();
        private Dictionary<int, RealFaceData> _faceMgmt = new Dictionary<int, RealFaceData>();

        private Coroutine _oneSecondRoutine;

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            EyeEarthQuakeActive = Config.Bind("Option", "Eye Shake Active", false, new ConfigDescription("Enable/Disable"));

            ExtraDynamicBoneColliderActive = Config.Bind("Option", "Extra DynamicBone Active", false, new ConfigDescription("Enable/Disable"));

            // WetDropActive = Config.Bind("Option", "Wet Active", false, new ConfigDescription("Enable/Disable"));

            // LiquidDropActive = Config.Bind("Option", "Liquid Active", false, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind("Option", "Real Breath Active", false, new ConfigDescription("Enable/Disable"));

            FaceActive = Config.Bind("Option", "Real Face Active", false, new ConfigDescription("Enable/Disable"));

            // WetInterval = Config.Bind("Wet", "Internval", 4.0f, new ConfigDescription("Wet Period", new AcceptableValueRange<float>(1.0f, 10.0f)));

            // WetAmplitude = Config.Bind("Wet", "Amplitude", 0.5f, new ConfigDescription("Wet Amplitude", new AcceptableValueRange<float>(0.0f, 1.0f)));

            BreathInterval = Config.Bind("Breath", "Internval", 2.5f, new ConfigDescription("Breath Interval", new AcceptableValueRange<float>(1.0f, 10.0f)));

            BreathAmplitude = Config.Bind("Breath", "Amplitude", 0.5f, new ConfigDescription("Breath Amplitude", new AcceptableValueRange<float>(0.1f, 1.0f)));

            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            // _breathCoroutine = StartCoroutine(Routine());
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
        #endregion

        #region Private Methods
        private void Init()
        {
            UnityEngine.Debug.Log($">> Init");

            UIUtility.Init();
            _loaded = true;

            string pluginDir = Path.GetDirectoryName(Info.Location);

            // 번들 파일 경로
            string bundlePath = Path.Combine(pluginDir, "realgirlbundle.unity3d");
            if (!File.Exists(bundlePath))
            {
                UnityEngine.Debug.Log($">> AssetBundle not found at: {bundlePath}");
                return;
            }

            AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                UnityEngine.Debug.Log(">> Failed to load AssetBundle!");
                return;
            }

            _faceDefaultBumpMap2 = bundle.LoadAsset<Texture2D>("Face_Default_BumpMap2");             
            _faceLaughBumpMap2 = bundle.LoadAsset<Texture2D>("Face_Laugh_BumpMap2");
            _facePukeBumpMap2 = bundle.LoadAsset<Texture2D>("Face_Puke_BumpMap2");
            _faceWrinkleBumpMap2 = bundle.LoadAsset<Texture2D>("Face_Wrinkle_BumpMap2");

            _faceStrongSmileEffectBumpMap2 = bundle.LoadAsset<Texture2D>("Face_Strong_Smile_BumpMap2");
            _eyeStrongBeautyEffectBumpMap2 = bundle.LoadAsset<Texture2D>("Eye_Strong_Beauty_BumpMap2");

            _defaultBumpMap2 = _faceDefaultBumpMap2;//MergeRGBAlphaMapsPartial(_faceSkeletonBumpMap2, _faceWrinkleBumpMap2);
            _laughBumpMap2 = _faceLaughBumpMap2;//MergeRGBAlphaMapsPartial(_faceLaughBumpMap2, _defaultBumpMap2);
            _pukeBumpMap2 = _facePukeBumpMap2;//MergeRGBAlphaMapsPartial(_facePukeBumpMap2, _defaultBumpMap2);

            // UnityEngine.Debug.Log($">> _faceDefaultBumpMap2 at: {_faceDefaultBumpMap2}");
            // UnityEngine.Debug.Log($">> _faceLaughBumpMap2 at: {_faceLaughBumpMap2}");
            // UnityEngine.Debug.Log($">> _facePukeBumpMap2 at: {_facePukeBumpMap2}");
            // UnityEngine.Debug.Log($">> _faceWrinkleBumpMap2 at: {_faceWrinkleBumpMap2}");
          
                        
            _faceMgmt.Add(0, new RealFaceData(_defaultBumpMap2,   0.8f));
            _faceMgmt.Add(1, new RealFaceData(_laughBumpMap2,     1.2f));
            _faceMgmt.Add(2, new RealFaceData(_laughBumpMap2,     1.2f));
            _faceMgmt.Add(3, new RealFaceData(_defaultBumpMap2,   0.8f));
            _faceMgmt.Add(4, new RealFaceData(_defaultBumpMap2,   1.0f));
            _faceMgmt.Add(5, new RealFaceData(_pukeBumpMap2,      1.1f));
            _faceMgmt.Add(6, new RealFaceData(_defaultBumpMap2,   1.0f));
            _faceMgmt.Add(7, new RealFaceData(_defaultBumpMap2,   1.3f));
            _faceMgmt.Add(8, new RealFaceData(_defaultBumpMap2,   1.3f));
            _faceMgmt.Add(9, new RealFaceData(_defaultBumpMap2,   1.2f));
            _faceMgmt.Add(10, new RealFaceData(_defaultBumpMap2,  1.2f));
            _faceMgmt.Add(11, new RealFaceData(_laughBumpMap2,    1.4f));
            _faceMgmt.Add(12, new RealFaceData(_pukeBumpMap2,     1.3f));
            _faceMgmt.Add(13, new RealFaceData(_laughBumpMap2,    1.2f));
            _faceMgmt.Add(14, new RealFaceData(_pukeBumpMap2,     1.2f));
            _faceMgmt.Add(15, new RealFaceData(_pukeBumpMap2,     1.2f));
            _faceMgmt.Add(16, new RealFaceData(_pukeBumpMap2,     1.2f));
            _faceMgmt.Add(17, new RealFaceData(_pukeBumpMap2,     1.2f));
            _faceMgmt.Add(18, new RealFaceData(_pukeBumpMap2,     1.2f));
            _faceMgmt.Add(19, new RealFaceData(_defaultBumpMap2,  1.2f));
            _faceMgmt.Add(20, new RealFaceData(_defaultBumpMap2,  0.8f));
            _faceMgmt.Add(21, new RealFaceData(_pukeBumpMap2,     1.2f));
            _faceMgmt.Add(22, new RealFaceData(_defaultBumpMap2,  0.8f));
            _faceMgmt.Add(23, new RealFaceData(_pukeBumpMap2,     1.2f));
            _faceMgmt.Add(24, new RealFaceData(_laughBumpMap2,    0.9f));
            _faceMgmt.Add(25, new RealFaceData(_pukeBumpMap2,     1.0f));

            _oneSecondRoutine = StartCoroutine(OneSecondRoutine());            
        }


        private void SceneInit()
        {
            foreach (var kvp in _ociCharMgmt)
            {
                var key = kvp.Key;
                RealHumanData value = kvp.Value;
                value.c_m_eye.Clear();

                if (value.cf_m_skin_body != null)
                {
                    value.cf_m_skin_body.SetFloat("_BumpScale", value.defaultBodyBumpScale1);
                    value.cf_m_skin_body.SetFloat("_BumpScale2", value.defaultBodyBumpScale2);
                }

                key.charInfo.StopCoroutine(value.coroutine);
            }

            _ociCharMgmt.Clear();
            _selectedOCIs.Clear();
        }

        IEnumerator OneSecondRoutine()
        {
            while (true) // 무한 반복
            {
                foreach(ObjectCtrlInfo ctrlInfo in _selectedOCIs)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var windData))
                    {                    
                    }
                }            
                    
                yield return new WaitForSeconds(1.5f); // 1.5초 대기
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
                    if (EyeEarthQuakeActive.Value == true)
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

                    // if (LiquidDropActive.Value)
                    // {
                    //     // ------------------------------
                    //     // 2️⃣ UV Scroll (상하 천천히)
                    //     // ------------------------------
                    //     Vector4 uv = _c_m_liquid_body.GetVector("_WeatheringUV");
                    //     uv.y += Time.deltaTime * 0.025f; // 전체 흐름 느리게
                    //     _c_m_liquid_body.SetVector("_WeatheringUV", uv);
                    //     if (_cf_m_skin_head != null)
                    //         _cf_m_skin_head.SetVector("_WeatheringUV", uv);
                    // }

                    if (BreathActive.Value)
                    {
                        float sinValue = (Mathf.Sin(time * BreathInterval.Value) + 1f) * 0.5f;
                        if (realHumanData.cf_m_skin_body != null)
                        {

                            float bumpScale1 = Mathf.Lerp(realHumanData.defaultBodyBumpScale1, realHumanData.defaultBodyBumpScale1 + BreathAmplitude.Value, sinValue);
                            float bumpScale2 = Mathf.Lerp(realHumanData.defaultBodyBumpScale2 - 0.4f, realHumanData.defaultBodyBumpScale2 + 0.5f, sinValue);

                            realHumanData.cf_m_skin_body.SetFloat("_BumpScale", bumpScale1);
                            realHumanData.cf_m_skin_body.SetFloat("_BumpScale2", bumpScale2);

                            // if (realHumanData.cf_m_skin_head != null)
                            // {
                            //     realHumanData.cf_m_skin_head.SetFloat("_BumpScale", bumpScale1 * 0.3f);
                            //     realHumanData.cf_m_skin_head.SetFloat("_BumpScale2", bumpScale2 * 0.3f);
                            // }
                        }


                        foreach (var ctrl in StudioAPI.GetSelectedControllers<PregnancyPlusCharaController>())
                        {
                            ctrl.infConfig.SetSliders(BellyTemplate.GetTemplate(1));
                            ctrl.infConfig.inflationSize = (1f - sinValue) * 10f * BreathAmplitude.Value;
                            ctrl.MeshInflate(new MeshInflateFlags(ctrl), "StudioSlider");
                        }
                    }

                    // if (WetDropActive.Value == true)
                    // {
                    //     float t = (time % WetInterval.Value) / WetInterval.Value;
                    //     // 1️⃣ WetBumpStreaks : 한 방향으로 내려가는 흐름
                    //     float easedBump = Mathf.Sin(t * Mathf.PI * 1.0f);
                    //     float wetBumpScale = Mathf.Lerp(wetMin, wetMax, easedBump);

                    //     if (_bodyShaderType == BODY_SHADER.HANMAN)
                    //     {
                    //         _cf_m_skin_body.SetFloat("_WetBumpStreaks", wetBumpScale);
                    //     }

                    //     if (_faceShaderType == BODY_SHADER.HANMAN)
                    //     {
                    //         _cf_m_skin_head.SetFloat("_WetBumpStreaks", wetBumpScale);
                    //     }
                    // }                  

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

        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar, RealHumanData realHumanData)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            AddRealEffect(ociChar, realHumanData);
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
                    // bone.m_Colliders.Clear();
                    foreach (var collider in extraColliders)
                        bone.m_Colliders.Add(collider);
                }

            }
        }

        private static void SupportEyeShake(OCIChar ociChar, RealHumanData realHumanData)
        {
            if (EyeEarthQuakeActive.Value)
                ociChar.charInfo.fbsCtrl.BlinkCtrl.BaseSpeed = 0.05f; // 작을수록 blink 속도가 높아짐..
            else
                ociChar.charInfo.fbsCtrl.BlinkCtrl.BaseSpeed = 0.15f;
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

        public static Texture2D MergeNormalMapsPartial(
            Texture2D normalA, Texture2D normalB,
            float weightA = 0.3f, float weightB = 0.7f,
            int startY = 0, int endY = -1)
        {
            int w = Mathf.Min(normalA.width, normalB.width);
            int hA = normalA.height;
            int hB = normalB.height;

            Color[] pixelsA = normalA.GetPixels();
            Color[] pixelsB = normalB.GetPixels();
            Color[] resultPixels = new Color[w * hA];

            int startY_B = hB - startY;

            Parallel.For(0, hA, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idxA = y * w + x;
                    Color colorA = pixelsA[idxA];
                    float mergedAlpha = colorA.a;

                    int texYB = startY_B - (hA - 1 - y);
                    if (texYB >= 0 && texYB < hB)
                    {
                        int idxB = texYB * w + x;
                        Color colorB = pixelsB[idxB];

                        // Height형 알파 병합
                        float centeredA = colorA.a - 0.5f;
                        float centeredB = colorB.a - 0.5f;
                        float merged = centeredA * weightA + centeredB * weightB;
                        mergedAlpha = Mathf.Clamp01(merged + 0.5f);
                    }

                    resultPixels[idxA] = new Color(colorA.r, colorA.g, colorA.b, mergedAlpha);
                }
            });

            Texture2D result = new Texture2D(w, hA, TextureFormat.RGBA32, false);
            result.SetPixels(resultPixels);
            result.Apply();
            return result;
        }

        // public static Texture2D MergeRGBAlphaMapsPartial(
        //         Texture2D rgbA,
        //         Texture2D rgbB,
        //         float weightA = 0.8f,
        //         float weightB = 0.2f,
        //         int startY = 0,
        //         int endY = -1,
        //         float rgbBase = 189f,
        //         float alphaBase = 0.5f)
        //     {
        //         int w = Mathf.Min(rgbA.width, rgbB.width);
        //         int hA = rgbA.height;
        //         int hB = rgbB.height;

        //         Color[] pixelsA = rgbA.GetPixels();
        //         Color[] pixelsB = rgbB.GetPixels();
        //         Color[] resultPixels = new Color[w * hA];

        //         int startY_B = hB - startY;

        //         if (endY < 0 || endY > hA)
        //             endY = hA;

        //         Parallel.For(startY, endY, y =>
        //         {
        //             for (int x = 0; x < w; x++)
        //             {
        //                 int idxA = y * w + x;

        //                 // 원본 G/B/A 값
        //                 float gA = pixelsA[idxA].g * 255f;
        //                 float bA = pixelsA[idxA].b * 255f;
        //                 float aA = pixelsA[idxA].a;

        //                 float gMerged = gA;
        //                 float bMerged = bA;
        //                 float aMerged = aA;

        //                 int texYB = startY_B - (hA - 1 - y);
        //                 if (texYB >= 0 && texYB < hB)
        //                 {
        //                     int idxB = texYB * w + x;

        //                     float gB = pixelsB[idxB].g * 255f;
        //                     float bB = pixelsB[idxB].b * 255f;
        //                     float aB = pixelsB[idxB].a;

        //                     // --- G, B 병합 (기준값 rgbBase) ---
        //                     gMerged = Mathf.Clamp((gA - rgbBase) * weightA + (gB - rgbBase) * weightB + rgbBase, 0f, 255f);
        //                     bMerged = Mathf.Clamp((bA - rgbBase) * weightA + (bB - rgbBase) * weightB + rgbBase, 0f, 255f);

        //                     // --- Alpha 병합 (기준값 alphaBase) ---
        //                     aMerged = Mathf.Clamp((aA - alphaBase) * weightA + (aB - alphaBase) * weightB + alphaBase, 0f, 1f);
        //                 }

        //                 // 최종 결과
        //                 resultPixels[idxA] = new Color(1f, gMerged / 255f, bMerged / 255f, aMerged);
        //             }
        //         });

        //         Texture2D result = new Texture2D(w, hA, TextureFormat.RGBA32, false);
        //         result.SetPixels(resultPixels);
        //         result.Apply();

        //         return result;
        //     }

        public static Texture2D MergeRGBAlphaMapsPartial(
            Texture2D rgbA, Texture2D rgbB,
            float weightA = 0.6f, float weightB = 0.4f,
            int startY = 0, int endY = -1,
            float rgbBase = 189f, float alphaBase = 0.5f,
            List<(float cx, float cy, float radius, float targetWeightB)> gradCenters = null)
        {
            int w = Mathf.Min(rgbA.width, rgbB.width);
            int hA = rgbA.height;
            int hB = rgbB.height;

            Color[] pixelsA = rgbA.GetPixels();
            Color[] pixelsB = rgbB.GetPixels();
            Color[] resultPixels = new Color[w * hA];

            int startY_B = hB - startY;

            if (endY < 0 || endY > hA)
                endY = hA;

            Parallel.For(startY, endY, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int idxA = y * w + x;

                    // --- gradCenters를 기반으로 weightB 보정 ---
                    float localWeightB = weightB;
                    if (gradCenters != null && gradCenters.Count > 0)
                    {
                        foreach (var (cx, cy, radius, targetWeightB) in gradCenters)
                        {
                            float dx = x - cx;
                            float dy = y - cy;
                            float dist = Mathf.Sqrt(dx * dx + dy * dy);
                            if (dist <= radius)
                            {
                                float t = Mathf.Clamp01(dist / radius);
                                float blended = 1.0f - (1.0f - targetWeightB) * t;
                                if (blended > localWeightB)
                                    localWeightB = blended;
                            }
                        }
                    }
                    float localWeightA = 1.0f - localWeightB;

                    // --- 원본 A 픽셀 ---
                    float gA = pixelsA[idxA].g * 255f;
                    float bA = pixelsA[idxA].b * 255f;
                    float aA = pixelsA[idxA].a;

                    float gMerged = gA;
                    float bMerged = bA;
                    float aMerged = aA;

                    int texYB = startY_B - (hA - 1 - y);
                    if (texYB >= 0 && texYB < hB)
                    {
                        int idxB = texYB * w + x;

                        float gB = pixelsB[idxB].g * 255f;
                        float bB = pixelsB[idxB].b * 255f;
                        float aB = pixelsB[idxB].a;

                        // --- 병합 연산 ---
                        gMerged = Mathf.Clamp((gA - rgbBase) * localWeightA + (gB - rgbBase) * localWeightB + rgbBase, 0f, 255f);
                        bMerged = Mathf.Clamp((bA - rgbBase) * localWeightA + (bB - rgbBase) * localWeightB + rgbBase, 0f, 255f);
                        aMerged = Mathf.Clamp((aA - alphaBase) * localWeightA + (aB - alphaBase) * localWeightB + alphaBase, 0f, 1f);
                    }

                    // --- 최종 결과 ---
                    resultPixels[idxA] = new Color(1f, gMerged / 255f, bMerged / 255f, aMerged);
                }
            });

            Texture2D result = new Texture2D(w, hA, TextureFormat.RGBA32, false);
            result.SetPixels(resultPixels);
            result.Apply();

            return result;
        }

        private static void ApplyDynamicBumpMapFace(OCIChar ociChar, RealHumanData realHumanData, int ptn)
        {
            if (realHumanData.cf_m_skin_head != null)
            {
                string bumpMapName = "_BumpMap2";
                string bumpScaleName = "_BumpScale2";

                Texture2D origin_texture = realHumanData.cf_m_skin_head.GetTexture("_BumpMap2") as Texture2D;

                if (origin_texture == null)
                {
                    origin_texture = realHumanData.cf_m_skin_head.GetTexture("_BumpMap") as Texture2D;
                    bumpMapName = "_BumpMap";
                    bumpScaleName = "_BumpScale";
                }

                if (FaceActive.Value)
                {
                    if (_self._faceMgmt.TryGetValue(ptn, out var realFaceData))
                    {
                        if (origin_texture != null)
                        {
                            realHumanData.cf_m_skin_head.SetTexture(bumpMapName, MergeRGBAlphaMapsPartial(MakeReadableTexture(origin_texture), realFaceData.skeletonTexture));
                            realHumanData.cf_m_skin_head.SetFloat(bumpScaleName, realFaceData.scale);                    
                        }
                    }
                } else
                {
                    realHumanData.cf_m_skin_head.SetTexture(bumpMapName, origin_texture);                             
                }
            }
        }        

        private static void AddRealEffect(OCIChar ociChar, RealHumanData realHumanData)
        {
            realHumanData.c_m_eye.Clear();
            SkinnedMeshRenderer[] sks = ociChar.guideObject.transformTarget.GetComponentsInChildren<SkinnedMeshRenderer>();

            Material pants = null;

            foreach (SkinnedMeshRenderer render in sks.ToList())
            {
                foreach (var mat in render.sharedMaterials)
                {
                    string name = mat.name.ToLower();
                    if (name.Contains("cf_m_skin_body"))
                    {
                        if (realHumanData.defaultBodyBumpScale1 == -99f)
                            realHumanData.defaultBodyBumpScale1 = render.material.GetFloat("_BumpScale");
                        else
                            render.material.SetFloat("_BumpScale", realHumanData.defaultBodyBumpScale1);

                        if (realHumanData.defaultBodyBumpScale2 == -99f)
                            realHumanData.defaultBodyBumpScale2 = render.material.GetFloat("_BumpScale2");
                        else
                            render.material.SetFloat("_BumpScale2", realHumanData.defaultBodyBumpScale2);

                        UnityEngine.Debug.Log($">> _BumpScale Body: + {render.material.GetFloat("_BumpScale")}");
                        UnityEngine.Debug.Log($">> _BumpScale2 Body: + {render.material.GetFloat("_BumpScale2")}");

                        // if (mat.shader.name.Contains("Hanmen/Next-Gen Body"))
                        // {
                        //     realHumanData.bodyShaderType = BODY_SHADER.HANMAN;
                        //     render.material.SetFloat("_WetBumpStreaks", 0.1f);
                        //     render.material.SetFloat("_ExGloss", 0.8f);
                        //     render.material.SetFloat("_Gloss", 0.1f);
                        // }
                        // else
                        // {
                        //     realHumanData.bodyShaderType = BODY_SHADER.DEFAULT;
                        // }

                        realHumanData.cf_m_skin_body = render.material;
                    }
                    else if (name.Contains("cf_m_skin_head"))
                    {
                        realHumanData.cf_m_skin_head = render.material;

                        UnityEngine.Debug.Log($">> _BumpScale Head: + {render.material.GetFloat("_BumpScale")}");
                        UnityEngine.Debug.Log($">> _BumpScale2 Head: + {render.material.GetFloat("_BumpScale2")}");

                        // if (mat.shader.name.Contains("Hanmen/Next-Gen Face"))
                        // {
                        //     realHumanData.faceShaderType = BODY_SHADER.HANMAN;
                        //     realHumanData.cf_m_skin_head.SetFloat("_WetBumpStreaks", 0.1f);
                        //     realHumanData.cf_m_skin_head.SetFloat("_ExGloss", 0.7f);
                        //     realHumanData.cf_m_skin_head.SetFloat("_Gloss", 0.1f);
                        // }
                        // else
                        // {
                        //     realHumanData.faceShaderType = BODY_SHADER.DEFAULT;
                        // }
                    }
                    else if (name.Contains("c_m_eye"))
                    {
                        realHumanData.c_m_eye.Add(render.material);
                    }
                    //else if (name.Contains("c_m_liquid_body"))
                    //{
                    //    realHumanData.c_m_liquid_body = mat;
                    //}

                    // UnityEngine.Debug.Log($">> found material: + {mat}");
                }
            }

            Texture2D headOriginTexture = realHumanData.cf_m_skin_head.GetTexture("_BumpMap2") as Texture2D;

            if (headOriginTexture == null)
            {
                headOriginTexture = realHumanData.cf_m_skin_head.GetTexture("_BumpMap") as Texture2D;
            }
            realHumanData.headOriginTexture = headOriginTexture;

            SupportEyeShake(ociChar, realHumanData);
            SupportExtraDynamicBones(ociChar, realHumanData);            
            ApplyDynamicBumpMapFace(ociChar, realHumanData, ociChar.charInfo.GetMouthPtn());
        }
        
        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   

                foreach(ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
                {
                    if (ctrlInfo != objectCtrlInfo)
                    {
                        OCIChar ociChar = ctrlInfo as OCIChar;
                        if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                        {
                            if (realHumanData.coroutine != null)
                            {
                                ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                            }
                            _self._ociCharMgmt.Remove(ociChar);
                        }
                    }
                }

                _self._selectedOCIs.Clear();
                _self._selectedOCIs.Add(objectCtrlInfo);     


                OCIChar ociChar2 = objectCtrlInfo as OCIChar;

                if (_self._ociCharMgmt.TryGetValue(ociChar2, out var realHumanData1))
                {
                    ociChar2.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar2, realHumanData1));
                }
                else
                {
                    RealHumanData realHumanData2 = new RealHumanData();
                    realHumanData2.coroutine = ociChar2.charInfo.StartCoroutine(_self.Routine(realHumanData2));                    
                    _self._ociCharMgmt.Add(ociChar2, realHumanData2);
                    ociChar2.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar2, realHumanData2));
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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                    {
                        ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
                    }
                    else
                    {
                        RealHumanData realHumanData2 = new RealHumanData();
                        realHumanData2.coroutine = ociChar.charInfo.StartCoroutine(_self.Routine(realHumanData2));
                        _self._ociCharMgmt.Add(ociChar, realHumanData2);
                        ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData2));
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
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                    {
                        if (realHumanData.coroutine != null)
                        {
                            ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                        }
                        realHumanData.c_m_eye.Clear();
                        if (realHumanData.cf_m_skin_body != null)
                        {
                            realHumanData.cf_m_skin_body.SetFloat("_bumpScale", realHumanData.defaultBodyBumpScale1);
                            realHumanData.cf_m_skin_body.SetFloat("_bumpScale2", realHumanData.defaultBodyBumpScale2);
                        }            

                        _self._ociCharMgmt.Remove(ociChar);
                    }
                }

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
                    if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                    {
                        if (realHumanData.coroutine != null)
                        {
                            ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                        }
                        realHumanData.c_m_eye.Clear();
                        if (realHumanData.cf_m_skin_body != null)
                        {
                            realHumanData.cf_m_skin_body.SetFloat("_bumpScale", realHumanData.defaultBodyBumpScale1);
                            realHumanData.cf_m_skin_body.SetFloat("_bumpScale2", realHumanData.defaultBodyBumpScale2);
                        }            

                        _self._ociCharMgmt.Remove(ociChar);
                    }
                }

                _self._selectedOCIs.Clear();  

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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                    {
                        ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                    {
                        ApplyDynamicBumpMapFace(ociChar, realHumanData, ptn);
                    }
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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                    {
                        ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
                    }
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
                    if (_self._ociCharMgmt.TryGetValue(ociChar, out var realHumanData))
                    {
                        ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
                    }
                }
            }
        }
        
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
    enum BODY_SHADER
    {
        HANMAN,
        DEFAULT
    }

    class RealFaceData
    {
        public Texture2D skeletonTexture;
        public float scale;

        public RealFaceData(Texture2D skeletonTexture, float scale)
        {
            this.skeletonTexture = skeletonTexture;
            this.scale = scale;            
        }               
    }
    
    #endregion
    class RealHumanData
    {
        public Coroutine coroutine;
        public DynamicBone_Ver02 rightBoob;
        public DynamicBone_Ver02 leftBoob;
        public DynamicBone_Ver02 rightButtCheek;
        public DynamicBone_Ver02 leftButtCheek;

        public Material cf_m_skin_head;
        public Material cf_m_skin_body;
        
        // public Material _c_m_liquid_body;

        public List<Material> c_m_eye = new List<Material>();

        public float defaultBodyBumpScale1 = -99f;
        public float defaultBodyBumpScale2 = -99f;

        public int expressionType = 0;

        public BODY_SHADER bodyShaderType = BODY_SHADER.DEFAULT;
        public BODY_SHADER faceShaderType = BODY_SHADER.DEFAULT;

        public Texture2D headOriginTexture;

        
        public RealHumanData()
        {
            
        }        
    }

}