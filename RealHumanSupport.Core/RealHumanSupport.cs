﻿using Studio;
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

        internal static ConfigEntry<bool> EyeShakeActive { get; private set; }

        internal static ConfigEntry<bool> ExBoneColliderActive { get; private set; }

        internal static ConfigEntry<bool> BreathActive { get; private set; }

        internal static ConfigEntry<bool> FaceBumpActive { get; private set; }

        internal static ConfigEntry<bool> BodyBumpActive { get; private set; }


        // breath
        internal static ConfigEntry<float> BreathAmplitude { get; private set; }

        internal static ConfigEntry<float> BreathInterval { get; private set; }

        private OCIChar _selectedOciChar;

        // end
        private Texture2D _faceExpressionFemaleBumpMap2;

        private Texture2D _faceExpressionMaleBumpMap2;
        
        private Texture2D _bodyStrongFemaleBumpMap2;

        private Texture2D _bodyStrongMaleBumpMap2;

        private ComputeShader _mergeComputeShader;

        private int _mouth_type;
        private int _eye_type;

        private AssetBundle _bundle;

        private RenderTexture _head_rt;

        private ComputeBuffer _head_areaBuffer;

        private RenderTexture _body_rt;
        private ComputeBuffer _body_areaBuffer;
        private Dictionary<int, RealHumanData> _ociCharMgmt = new Dictionary<int, RealHumanData>();
        private Dictionary<int, RealFaceData> _faceMouthMgmt = new Dictionary<int, RealFaceData>();
        private Dictionary<int, RealFaceData> _faceEyesMgmt = new Dictionary<int, RealFaceData>();
        private Coroutine _CheckRotationRoutine;

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            EyeShakeActive = Config.Bind("Option", "Eye shake active", true, new ConfigDescription("Enable/Disable"));

            ExBoneColliderActive = Config.Bind("Option", "Extra dynamicBone active", false, new ConfigDescription("Enable/Disable"));

            BreathActive = Config.Bind("Option", "Real breath effect active", true, new ConfigDescription("Enable/Disable"));

            FaceBumpActive = Config.Bind("Option", "Real face effect active", true, new ConfigDescription("Enable/Disable"));

            BodyBumpActive = Config.Bind("Option", "Real body effect active", true, new ConfigDescription("Enable/Disable"));

            BreathInterval = Config.Bind("Breath", "Cycle", 1.5f, new ConfigDescription("Breath Interval", new AcceptableValueRange<float>(1.0f,  3.0f)));;

            BreathAmplitude = Config.Bind("Breath", "Amplitude", 0.5f, new ConfigDescription("Breath Amplitude", new AcceptableValueRange<float>(0.1f, 1.0f)));

            
            _self = this;

            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmonyInstance = HarmonyExtensions.CreateInstance(GUID);
            harmonyInstance.PatchAll(Assembly.GetExecutingAssembly());

            _CheckRotationRoutine = StartCoroutine(CheckRotationRoutine());
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
            // UnityEngine.Debug.Log($">> Init");

            UIUtility.Init();
            _loaded = true;

            // 배포용 번들 파일 경로
            string bundlePath = Application.dataPath + "/../abdata/realgirl/realgirlbundle.unity3d";

            _bundle = AssetBundle.LoadFromFile(bundlePath);
            if (_bundle == null)
            {
                UnityEngine.Debug.Log(">> Failed to load AssetBundle!");
                Logger.LogMessage($"Please Install realgirl.zipmod!");
                return;
            } else
            {
                UnityEngine.Debug.Log($">> Load {_bundle}");
            }

            _bodyStrongFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_F_BumpMap2");
            _bodyStrongMaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Body_Strong_M_BumpMap2");
            
            _faceExpressionFemaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_F_BumpMap2");
            _faceExpressionMaleBumpMap2 = _bundle.LoadAsset<Texture2D>("Face_Expression_M_BumpMap2");

            _mergeComputeShader = _bundle.LoadAsset<ComputeShader>("MergeTextures.compute");

            // UnityEngine.Debug.Log($">> _mergeComputeShader {_mergeComputeShader}");
   
            _faceMouthMgmt.Add(0, new RealFaceData());
            _faceMouthMgmt.Add(1, new RealFaceData(InitBArea(512, 610, 120, 80, 0.4f)));
            _faceMouthMgmt.Add(2, new RealFaceData(InitBArea(512, 610, 140, 90, 0.4f)));
            _faceMouthMgmt.Add(3, new RealFaceData());
            _faceMouthMgmt.Add(4, new RealFaceData(InitBArea(512, 690, 60, 50, 0.4f)));
            _faceMouthMgmt.Add(5, new RealFaceData(InitBArea(512, 690, 60, 50, 0.4f)));
            _faceMouthMgmt.Add(6, new RealFaceData(InitBArea(512, 690, 60, 50, 0.4f)));
            _faceMouthMgmt.Add(7, new RealFaceData(InitBArea(470, 590, 60, 40)));
            _faceMouthMgmt.Add(8, new RealFaceData(InitBArea(570, 590, 60, 40)));
            _faceMouthMgmt.Add(9, new RealFaceData(InitBArea(512, 590, 100, 60)));
            _faceMouthMgmt.Add(10, new RealFaceData(InitBArea(520, 630, 40, 40), InitBArea(320, 660, 80, 120, 1f), InitBArea(690, 660, 80, 120, 1f)));
            _faceMouthMgmt.Add(11, new RealFaceData(InitBArea(520, 630, 40, 40), InitBArea(320, 660, 80, 120, 1f), InitBArea(690, 660, 80, 120, 1f)));
            _faceMouthMgmt.Add(12, new RealFaceData(InitBArea(512, 690, 90, 60, 0.5f)));
            _faceMouthMgmt.Add(13, new RealFaceData(InitBArea(320, 660, 80, 120, 1.5f), InitBArea(690, 660, 80, 120, 1.5f)));
            _faceMouthMgmt.Add(14, new RealFaceData());
            _faceMouthMgmt.Add(15, new RealFaceData(InitBArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(16, new RealFaceData(InitBArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(17, new RealFaceData(InitBArea(320, 660, 80, 120, 2f), InitBArea(690, 660, 80, 120, 2f)));
            _faceMouthMgmt.Add(18, new RealFaceData(InitBArea(320, 660, 80, 120, 2f), InitBArea(690, 660, 80, 120, 2f)));
            _faceMouthMgmt.Add(19, new RealFaceData());
            _faceMouthMgmt.Add(20, new RealFaceData());
            _faceMouthMgmt.Add(21, new RealFaceData(InitBArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(22, new RealFaceData());
            _faceMouthMgmt.Add(23, new RealFaceData(InitBArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(24, new RealFaceData(InitBArea(512, 690, 90, 60, 0.4f)));
            _faceMouthMgmt.Add(25, new RealFaceData());

            _faceEyesMgmt.Add(0, new RealFaceData()); //
            _faceEyesMgmt.Add(1, new RealFaceData(InitBArea(310, 470, 80, 60, 0.3f), InitBArea(700, 470, 80, 60, 0.3f), InitBArea(455, 505, 60, 80, 0.6f), InitBArea(580, 505, 60, 80, 0.6f))); //
            _faceEyesMgmt.Add(2, new RealFaceData(InitBArea(310, 470, 80, 60, 0.5f), InitBArea(700, 470, 80, 60, 0.5f), InitBArea(455, 505, 60, 80, 0.6f), InitBArea(580, 505, 60, 80, 0.6f))); //
            _faceEyesMgmt.Add(3, new RealFaceData());
            _faceEyesMgmt.Add(4, new RealFaceData());
            _faceEyesMgmt.Add(5, new RealFaceData());
            _faceEyesMgmt.Add(6, new RealFaceData());
            _faceEyesMgmt.Add(7, new RealFaceData(InitBArea(310, 470, 80, 60, 1f), InitBArea(700, 470, 80, 60, 1f), InitBArea(455, 505, 60, 80, 0.8f), InitBArea(580, 505, 60, 80, 0.8f))); //
            _faceEyesMgmt.Add(8, new RealFaceData(InitBArea(310, 470, 60, 60, 1f), InitBArea(455, 505, 80, 60, 0.6f), InitBArea(580, 505, 80, 60, 0.6f))); //
            _faceEyesMgmt.Add(9, new RealFaceData(InitBArea(700, 470, 60, 40, 0.8f), InitBArea(455, 505, 80, 60, 0.6f), InitBArea(580, 505, 80, 60, 0.6f))); //
            _faceEyesMgmt.Add(10, new RealFaceData());
            _faceEyesMgmt.Add(11, new RealFaceData()); //
            _faceEyesMgmt.Add(12, new RealFaceData(InitBArea(310, 470, 80, 60, 0.8f), InitBArea(455, 505, 80, 60, 0.6f))); //
            _faceEyesMgmt.Add(13, new RealFaceData(InitBArea(700, 470, 80, 60, 0.8f), InitBArea(580, 505, 80, 60, 0.6f))); //

            _self._head_areaBuffer = new ComputeBuffer(8, sizeof(float) * 6);
            _self._body_areaBuffer = new ComputeBuffer(16, sizeof(float) * 6);

                
        }

        private void SceneInit()
        {
            // UnityEngine.Debug.Log($">> SceneInit()");
            foreach (var kvp in _ociCharMgmt)
            {
                var key = kvp.Key;
                RealHumanData value = kvp.Value;
                value.c_m_eye.Clear();
                if (value != null && value.ociChar != null && value.coroutine != null)
                    value.ociChar.charInfo.StopCoroutine(value.coroutine);
            }

            _mouth_type = 0;
            _eye_type = 0;

            _ociCharMgmt.Clear();
        }

        IEnumerator CheckRotationRoutine()
        {
            while (true) // 무한 반복
            {
                if (_selectedOciChar != null && !Input.GetMouseButton(0))
                {
                    if (_ociCharMgmt.TryGetValue(_selectedOciChar.GetHashCode(), out var realHumanData))
                    {
                        if (realHumanData.m_skin_body == null || realHumanData.m_skin_head == null)
                            realHumanData = GetMaterials(_selectedOciChar, realHumanData);

                        if (
                            (GetBoneRotationFromIK(realHumanData.lk_left_foot_bone)._q != realHumanData.prev_lk_left_foot_rot) ||
                            (GetBoneRotationFromIK(realHumanData.lk_right_foot_bone)._q != realHumanData.prev_lk_right_foot_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_left_foot_bone)._q != realHumanData.prev_fk_left_foot_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_right_foot_bone)._q != realHumanData.prev_fk_right_foot_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_left_knee_bone)._q != realHumanData.prev_fk_left_knee_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_right_knee_bone)._q != realHumanData.prev_fk_right_knee_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone)._q != realHumanData.prev_fk_left_thigh_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone)._q != realHumanData.prev_fk_right_thigh_rot) ||                          
                            (GetBoneRotationFromFK(realHumanData.fk_spine01_bone)._q != realHumanData.prev_fk_spine01_rot) || 
                            (GetBoneRotationFromFK(realHumanData.fk_spine02_bone)._q != realHumanData.prev_fk_spine02_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_head_bone)._q != realHumanData.prev_fk_head_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone)._q != realHumanData.prev_fk_left_shoulder_rot) ||
                            (GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone)._q != realHumanData.prev_fk_right_shoulder_rot)
                        )
                        {
                            DoBodyRealEffect(_selectedOciChar, realHumanData);
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
            
            SupportEyeShake(ociChar, realHumanData);
            SupportExtraDynamicBones(ociChar, realHumanData);
            DoBodyRealEffect(ociChar, realHumanData);
            DoFaceRealEffect(ociChar, realHumanData);
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
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32);
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
            if (ExBoneColliderActive.Value && ociChar.charInfo.sex == 1)
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

private static Texture2D MergeRGBAlphaMaps(
    Texture2D rgbA,
    Texture2D rgbB,
    List<BArea> areas = null)
{
    int w = Mathf.Min(rgbA.width, rgbB.width);
    int h = Mathf.Min(rgbA.height, rgbB.height);

    bool useArea = (areas != null && areas.Count > 0);

    // A, B 픽셀 로드
    Color[] cur = rgbA.GetPixels(0, 0, w, h);
    Color[] B   = rgbB.GetPixels(0, 0, w, h);

    // 영역 없으면 A 그대로 반환
    if (!useArea)
    {
        Texture2D onlyA = new Texture2D(w, h, TextureFormat.RGBA32, false);
        onlyA.SetPixels(cur);
        onlyA.Apply();
        return onlyA;
    }

    // ===== area 순차 적용 =====
    foreach (var area in areas)
    {
        float rx = area.RadiusX > 0f ? area.RadiusX : area.RadiusY;
        float ry = area.RadiusY > 0f ? area.RadiusY : area.RadiusX;
        if (rx <= 0f || ry <= 0f)
            continue;

        float invRx = 1f / rx;
        float invRy = 1f / ry;

        // Unity Y → 이미지 Y 보정
        float areaY = (h - 1) - area.Y;

        // 중심 강도값
        float baseStrength = area.Strong * area.BumpBooster;

        Parallel.For(0, h, y =>
        {
            float dy = y - areaY;

            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;

                float dx = x - area.X;

                // ===== ellipse test =====
                float nx = dx * invRx;
                float ny = dy * invRy;
                float ellipseVal = nx * nx + ny * ny;
                if (ellipseVal > 1f)
                    continue;

                // ===== falloff mask =====
                float t = Mathf.Sqrt(ellipseVal);
                
                // mask = 중심 강도값 * falloff
                float mask = baseStrength * (1f - t * t * t);
                mask = Mathf.Clamp01(mask);

                float weightB = mask;
                float weightA = 1f - weightB;

                Color a = cur[idx];
                Color b = B[idx];

                // ===== Python식 pure blending =====
                float gMerged = a.g * weightA + b.g * weightB;
                float bMerged = a.b * weightA + b.b * weightB;
                float aMerged = a.a * weightA + b.a * weightB;

                // R은 255 고정
                cur[idx] = new Color(
                    1f,
                    Mathf.Clamp01(gMerged),
                    Mathf.Clamp01(bMerged),
                    Mathf.Clamp01(aMerged)
                );
            }
        });
    }

    // ===== 결과 Texture 생성 =====
    Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
    result.SetPixels(cur);
    result.Apply();
    return result;
}
        // private static Texture2D MergeRGBAlphaMaps(
        //     Texture2D rgbA,
        //     Texture2D rgbB,
        //     List<BArea> areas = null)
        // {
        //     int w = Mathf.Min(rgbA.width, rgbB.width);
        //     int h = Mathf.Min(rgbA.height, rgbB.height);

        //     bool useArea = (areas != null && areas.Count > 0);

        //     // A, B 픽셀 로드
        //     Color[] cur = rgbA.GetPixels(0, 0, w, h);
        //     Color[] B   = rgbB.GetPixels(0, 0, w, h);

        //     // 영역 없으면 A 그대로 반환
        //     if (!useArea)
        //     {
        //         Texture2D onlyA = new Texture2D(w, h, TextureFormat.RGBA32, false);
        //         onlyA.SetPixels(cur);
        //         onlyA.Apply();
        //         return onlyA;
        //     }

        //     // ===== area 순차 적용 =====
        //     foreach (var area in areas)
        //     {
        //         float rx = area.RadiusX > 0f ? area.RadiusX : area.RadiusY;
        //         float ry = area.RadiusY > 0f ? area.RadiusY : area.RadiusX;
        //         if (rx <= 0f || ry <= 0f)
        //             continue;

        //         float invRx = 1f / rx;
        //         float invRy = 1f / ry;

        //         // Unity Y → 이미지 Y 보정
        //         float areaY = (h - 1) - area.Y;

        //         // mask 증폭용
        //         float strongMul = area.Strong * area.BumpBooster;

        //         Parallel.For(0, h, y =>
        //         {
        //             float dy = y - areaY;

        //             for (int x = 0; x < w; x++)
        //             {
        //                 int idx = y * w + x;

        //                 float dx = x - area.X;

        //                 // ===== ellipse test =====
        //                 float nx = dx * invRx;
        //                 float ny = dy * invRy;
        //                 float ellipseVal = nx * nx + ny * ny;
        //                 if (ellipseVal > 1f)
        //                     continue;

        //                 // ===== falloff mask =====
        //                 // t = 0(center) ~ 1(edge)
        //                 float t = Mathf.Sqrt(ellipseVal);

        //                 // mask = 1 - t^3
        //                 float mask = 1f - (t * t * t);

        //                 // ===== strong / booster → mask에만 적용 =====
        //                 // float boostedMask = mask * (1f + strongMul);
        //                 float boostedMask = Mathf.Clamp01(mask + strongMul * mask);
        //                 boostedMask = Mathf.Clamp01(boostedMask);

        //                 float weightB = boostedMask;
        //                 float weightA = 1f - weightB;

        //                 Color a = cur[idx];
        //                 Color b = B[idx];

        //                 // ===== Python식 pure blending =====
        //                 float gMerged = a.g * weightA + b.g * weightB;
        //                 float bMerged = a.b * weightA + b.b * weightB;
        //                 float aMerged = a.a * weightA + b.a * weightB;

        //                 // R은 255 고정
        //                 cur[idx] = new Color(
        //                     1f,
        //                     Mathf.Clamp01(gMerged),
        //                     Mathf.Clamp01(bMerged),
        //                     Mathf.Clamp01(aMerged)
        //                 );
        //             }
        //         });
        //     }

        //     // ===== 결과 Texture 생성 =====
        //     Texture2D result = new Texture2D(w, h, TextureFormat.RGBA32, false);
        //     result.SetPixels(cur);
        //     result.Apply();
        //     return result;
        // }


        private static PositionData GetBoneRotationFromIK(OCIChar.IKInfo info)
        {
            Transform t = info.guideObject.transform;
            Vector3 fwd = t.forward;

            // 앞 / 뒤 (Pitch)
            Vector3 fwdYZ = Vector3.ProjectOnPlane(fwd, Vector3.right).normalized;
            float pitch = Vector3.SignedAngle(
                Vector3.forward,
                fwdYZ,
                Vector3.right
            );

            // 좌 / 우 (sideZ)
            Vector3 right = t.right;

            Vector3 rightXY = Vector3.ProjectOnPlane(right, Vector3.forward).normalized;

            float sideZ = Vector3.SignedAngle(
                Vector3.right,
                rightXY,
                Vector3.forward
            );

            PositionData data = new PositionData(info.guideObject.transform.rotation, pitch, sideZ);
            return data;
        }


        private static PositionData GetBoneRotationFromFK(OCIChar.BoneInfo info)
        {
            Transform t = info.guideObject.transform;
            Vector3 fwd = t.forward;

            // 앞 / 뒤 (Pitch)
            Vector3 fwdYZ = Vector3.ProjectOnPlane(fwd, Vector3.right).normalized;
            float pitch = Vector3.SignedAngle(
                Vector3.forward,
                fwdYZ,
                Vector3.right
            );

            // 좌 / 우 (sideZ)
            Vector3 right = t.right;

            Vector3 rightXY = Vector3.ProjectOnPlane(right, Vector3.forward).normalized;

            float sideZ = Vector3.SignedAngle(
                Vector3.right,
                rightXY,
                Vector3.forward
            );

            PositionData data = new PositionData(info.guideObject.transform.rotation, pitch, sideZ);
            return data;
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

            if (realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as Texture2D == null)
                headOriginTexture = ConvertToTexture2D(realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as RenderTexture);
            else 
                headOriginTexture = realHumanData.m_skin_head.GetTexture(realHumanData.head_bumpmap_name) as Texture2D;

            if (realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as Texture2D == null)
                bodyOriginTexture = ConvertToTexture2D(realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as RenderTexture);
            else
                bodyOriginTexture = realHumanData.m_skin_body.GetTexture(realHumanData.body_bumpmap_name) as Texture2D;

            // realHumanData.body_bumpScale2 = realHumanData.m_skin_body.GetFloat("_BumpScale2");

            realHumanData.headOriginTexture = SetTextureSize(MakeReadableTexture(headOriginTexture), _self._faceExpressionFemaleBumpMap2.width, _self._faceExpressionFemaleBumpMap2.height);
            realHumanData.bodyOriginTexture = SetTextureSize(MakeReadableTexture(bodyOriginTexture), _self._bodyStrongFemaleBumpMap2.width, _self._bodyStrongFemaleBumpMap2.height);
    
            return realHumanData;
        }

        private static RealHumanData GetMaterials(OCIChar ociChar, RealHumanData realHumanData)
        {
            SkinnedMeshRenderer[] sks = ociChar.guideObject.transformTarget.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (SkinnedMeshRenderer render in sks.ToList())
            {
                foreach (var mat in render.sharedMaterials)
                {
                    string name = mat.name.ToLower();

                    if (name.Contains("_m_skin_body"))
                    {
                        realHumanData.m_skin_body = mat;
                    }
                    else if (name.Contains("_m_skin_head"))
                    {
                        realHumanData.m_skin_head = mat;
                    }
                    else if (name.Contains("c_m_eye"))
                    {
                        realHumanData.c_m_eye.Add(mat);
                    }
                }
            }

            return realHumanData;
        }

        private static  BArea InitBArea(float x, float y, float radiusX, float radiusY, float bumpBooster=1.0f)
        {
            return new BArea
            {
                X = x,
                Y = y,
                RadiusX = radiusX,
                RadiusY = radiusY,
                Strong = 1.0f,
                BumpBooster = bumpBooster
            };
        }

        private static RealHumanData InitRealHumanData(OCIChar ociChar, RealHumanData realHumanData)
        {
            realHumanData.c_m_eye.Clear();
            SkinnedMeshRenderer[] sks = ociChar.guideObject.transformTarget.GetComponentsInChildren<SkinnedMeshRenderer>();

            realHumanData = GetMaterials(ociChar, realHumanData);
    
            if (realHumanData.m_skin_body.GetTexture("_BumpMap2") != null)
            {
                realHumanData.body_bumpmap_name = "_BumpMap2";
            } 
            else if (realHumanData.m_skin_body.GetTexture("_BumpMap") != null)
            {
                realHumanData.body_bumpmap_name = "_BumpMap";
            }
            else
            {
                realHumanData.body_bumpmap_name = "";
            }
            
            if (realHumanData.m_skin_head.GetTexture("_BumpMap2") != null)
            {
                realHumanData.head_bumpmap_name = "_BumpMap2";
            }
            else if (realHumanData.m_skin_head.GetTexture("_BumpMap") != null)
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
                
                // if (ociChar.charInfo.sex == 1) // female
                // {
                //     realHumanData.tf_n_height = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_N_height");
                // }
                // else // male
                // {
                //     realHumanData.tf_n_height = ociChar.charInfo.objBodyBone.transform.FindLoop("cm_N_height");
                // }

                // realHumanData.tf_j_r_foot = ociChar.charInfo.objBodyBone.transform.FindLoop("cf_J_Foot01_R");

                foreach (OCIChar.BoneInfo bone in ociChar.listBones)
                {
                    if (bone.guideObject != null && bone.guideObject.transformTarget != null) {
                        // UnityEngine.Debug.Log($">> bone : + {bone.guideObject.transformTarget.name}");
                        if(bone.guideObject.transformTarget.name.Contains("_J_Spine01"))
                        {
                            realHumanData.fk_spine01_bone = bone; // 하단
                        }
                        else if(bone.guideObject.transformTarget.name.Contains("_J_Spine02"))
                        {
                            realHumanData.fk_spine02_bone = bone; // 상단
                        }
                        // else if (bone.guideObject.transformTarget.name.Contains("_J_ArmUp00_L"))
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Shoulder_L"))
                        {
                            realHumanData.fk_left_shoulder_bone = bone;
                        }
                        // else if (bone.guideObject.transformTarget.name.Contains("_J_ArmUp00_R"))
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Shoulder_R"))
                        {
                            realHumanData.fk_right_shoulder_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_LegUp00_L"))
                        {
                            realHumanData.fk_left_thigh_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_LegUp00_R"))
                        {
                            realHumanData.fk_right_thigh_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_LegLow01_L"))
                        {
                            realHumanData.fk_left_knee_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_LegLow01_R"))
                        {
                            realHumanData.fk_right_knee_bone = bone;
                        }                        
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Hand_L"))
                        {
                            realHumanData.fk_left_hand_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Hand_R"))
                        {
                            realHumanData.fk_right_hand_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Head"))
                        {
                            realHumanData.fk_head_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Neck"))
                        {
                            realHumanData.fk_neck_bone = bone;
                        }
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Foot01_L"))
                        {
                            realHumanData.fk_left_foot_bone = bone;
                        }   
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Foot01_R"))
                        {
                            realHumanData.fk_right_foot_bone = bone;
                        }                 
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Toes01_L"))
                        {
                            realHumanData.fk_left_toes_bone = bone;
                        }   
                        else if (bone.guideObject.transformTarget.name.Contains("_J_Toes01_R"))
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
            if (realHumanData.m_skin_head != null)
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

                    List<BArea> areas = new List<BArea>();

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
                        int kernel = _self._mergeComputeShader.FindKernel("CSMain");
                        int currentAreaCount = 0;
                        int w = 1024;
                        int h = 1024;
                        // Texture texA;
                        // Texture texB;
                        // RenderTexture 초기화 및 재사용
                        if (_self._head_rt == null || _self._head_rt.width != w || _self._head_rt.height != h)
                        {
                            if (_self._head_rt != null) _self._head_rt.Release();
                            _self._head_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                            _self._head_rt.enableRandomWrite = true;
                            _self._head_rt.Create();
                        }        

                        // w = Mathf.Min(texA.width, texB.width);
                        // h = Mathf.Min(texA.height, texB.height);

                        //Init(w, h);

                        // 영역 데이터가 변경된 경우만 업데이트
                    // 영역 데이터가 변경된 경우만 업데이트
                        if (areas.Count > 0)
                        {                         
                            _self._head_areaBuffer.SetData(areas.ToArray());
                            // 셰이더 파라미터 설정
                            _self._mergeComputeShader.SetInt("Width", w);
                            _self._mergeComputeShader.SetInt("Height", h);
                            _self._mergeComputeShader.SetInt("AreaCount", currentAreaCount);
                            _self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                            _self._mergeComputeShader.SetTexture(kernel, "TexB", express_texture);
                            _self._mergeComputeShader.SetTexture(kernel, "Result", _self._head_rt);
                            _self._mergeComputeShader.SetBuffer(kernel, "Areas", _self._head_areaBuffer);

                            // Dispatch 실행
                            _self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);

                            // 결과를 바로 Material에 적용 (CPU로 복사 안 함)    
                            realHumanData.m_skin_head.SetTexture(realHumanData.head_bumpmap_name, _self._head_rt);

                            // Texture2D merged =  MergeRGBAlphaMaps(origin_texture, express_texture, areas);
                            // realHumanData.m_skin_head.SetTexture(realHumanData.head_bumpmap_name, merged);
                            // SaveAsPNG(merged, "./face_merged.png");
                    
                        }
                    }                
                } 
                else
                {
                    realHumanData.m_skin_head.SetTexture(realHumanData.head_bumpmap_name, realHumanData.headOriginTexture);
                }
            }
        }

            private static float CombineBySign(float a, float b)
            {
                bool sameSign = (a >= 0 && b >= 0) || (a < 0 && b < 0);

                if (sameSign)
                    return Math.Abs(Math.Abs(a) - Math.Abs(b)); // 동일 부호: 절댓값 빼기
                else
                    return Math.Abs(Math.Abs(a) + Math.Abs(b)); // 부호 다름: 절댓값 더하기
            } 
       private static void DoBodyRealEffect(OCIChar ociChar, RealHumanData realHumanData)

        {         
            // UnityEngine.Debug.Log($">> DoBodyRealEffect {_self._ociCharMgmt.Count}, {realHumanData.m_skin_head}, {realHumanData.m_skin_body}");
            if (realHumanData.m_skin_head == null || realHumanData.m_skin_body == null)
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
                PositionData lk_left_foot = GetBoneRotationFromIK(realHumanData.lk_left_foot_bone);
                PositionData lk_right_foot = GetBoneRotationFromIK(realHumanData.lk_right_foot_bone);
                
                PositionData fk_left_foot = GetBoneRotationFromFK(realHumanData.fk_left_foot_bone);
                PositionData fk_right_foot = GetBoneRotationFromFK(realHumanData.fk_right_foot_bone);

                PositionData fk_left_knee = GetBoneRotationFromFK(realHumanData.fk_left_knee_bone);
                PositionData fk_right_knee = GetBoneRotationFromFK(realHumanData.fk_right_knee_bone);
                
                PositionData fk_left_thigh = GetBoneRotationFromFK(realHumanData.fk_left_thigh_bone);
                PositionData fk_right_thigh = GetBoneRotationFromFK(realHumanData.fk_right_thigh_bone);

                PositionData fk_spine01 = GetBoneRotationFromFK(realHumanData.fk_spine01_bone);
                PositionData fk_spine02 = GetBoneRotationFromFK(realHumanData.fk_spine02_bone);

                PositionData fk_left_shoulder= GetBoneRotationFromFK(realHumanData.fk_left_shoulder_bone);
                PositionData fk_right_shoulder= GetBoneRotationFromFK(realHumanData.fk_right_shoulder_bone);

                PositionData fk_head= GetBoneRotationFromFK(realHumanData.fk_head_bone);

                float left_shin_bs = 0.0f;
                float left_calf_bs = 0.0f;
                float left_butt_bs = 0.0f;
                float left_thigh_ft_bs = 0.0f;
                float left_thigh_bk_bs = 0.0f;
                float left_thigh_inside_bs = 0.0f;
                float left_spine_bs = 0.0f;
                float left_neckline_bs = 0.0f;

                float right_shin_bs = 0.0f;
                float right_calf_bs = 0.0f;                
                float right_butt_bs = 0.0f;
                float right_thigh_ft_bs = 0.0f;
                float right_thigh_bk_bs = 0.0f;
                float right_thigh_inside_bs = 0.0f;
                float right_spine_bs = 0.0f;
                float right_neckline_bs = 0.0f;

                float spine_bs = 0.0f;
                float ribs_bs = 0.0f;
                
                float neck_spine_bs = 0.0f;
                float neckline_bs = 0.0f;                

                float bumpscale = 0.0f;

                if (ociChar.oiCharInfo.enableFK) {
     
                    // 허벅지 왼쪽
                    if (fk_left_thigh._front > 1.0f)
                    {   // 뒷방향
                        bumpscale = Math.Min(Remap(fk_left_thigh._front, 0.0f, 120.0f, 0.1f, 1.0f), 1.0f);                     
                        left_butt_bs += bumpscale * 0.8f;
                        left_thigh_bk_bs += bumpscale * 0.8f;                                               
                    } 
                    else
                    {   // 앞방향
                        bumpscale = Math.Min(Remap(Math.Abs(fk_left_thigh._front), 0.0f, 120.0f, 0.0f, 1.0f), 1.0f);
                        if (fk_left_thigh._front < -130.0f)
                        {                            
                            left_thigh_bk_bs += bumpscale * 1.0f;                          
                        }                        
                        left_thigh_inside_bs += bumpscale * 1.0f;
                        left_thigh_ft_bs += bumpscale * 0.8f;                                                                      
                    }
                    // 허벅지 오른쪽
                    if (fk_right_thigh._front > 1.0f)
                    {   // 뒷방향      
                        bumpscale = Math.Min(Remap(fk_right_thigh._front, 0.0f, 120.0f, 0.1f, 1.0f), 1.0f);
                        right_butt_bs += bumpscale * 0.8f;
                        right_thigh_bk_bs += bumpscale * 0.8f; 
                    }  
                    else
                    {   // 앞방향
                        bumpscale = Math.Min(Remap(Math.Abs(fk_right_thigh._front), 0.0f, 120.0f, 0.0f, 1.0f), 1.0f);
                        if (fk_right_thigh._front < -130.0f)
                        {                            
                            right_thigh_bk_bs += bumpscale * 1.0f; 
                        } 
                        right_thigh_inside_bs += bumpscale * 1.0f;                       
                        right_thigh_ft_bs += bumpscale * 0.8f;  
                    }
                    
                    // 무릎 왼쪽
                    if (fk_left_knee._front > 1.0f)
                    {   // 뒷방향       
                        float strong = CombineBySign(fk_left_thigh._front, fk_left_knee._front);
                        bumpscale = Math.Min(Remap(strong, 0.0f, 90.0f, 0.1f, 1.0f), 1.0f);
                        left_thigh_ft_bs += bumpscale * 0.4f;
                        left_thigh_bk_bs += bumpscale * 0.6f;
                        UnityEngine.Debug.Log($">> fk_left_knee, strong: {strong}, scale: {left_thigh_bk_bs}");                                                                        
                    } 
                    // 무릎 오른쪽
                    if (fk_right_knee._front > 1.0f)
                    {   // 뒷방향      
                        float strong = CombineBySign(fk_right_thigh._front, fk_right_knee._front);
                        bumpscale = Math.Min(Remap(strong, 0.0f, 90.0f, 0.1f, 1.0f), 1.0f);
                        right_thigh_ft_bs += bumpscale * 0.4f;
                        right_thigh_bk_bs += bumpscale * 0.6f;                         
                    }  

                    // 허리
                    if (fk_spine02._front > 1.0f)
                    {
                        bumpscale = Math.Min(Remap(fk_spine02._front, 0.0f, 90.0f, 0.0f, 1.0f), 1.0f);
                        spine_bs += bumpscale * 1.0f;
                    } 
                    else
                    {
                        bumpscale = Math.Min(Remap(Math.Abs(fk_spine02._front), 0.0f, 90.0f, 0.0f, 1.0f), 1.0f);
                        ribs_bs += bumpscale * 1.0f;
                    }

                    if (fk_spine02._side > 1.0f)
                    {   // 왼쪽 기울기                                            
                        bumpscale = Math.Min(Remap(fk_spine02._side, 0.0f, 70.0f, 0.0f, 1.0f), 1.0f);
                        left_spine_bs += bumpscale * 0.2f;                  
                    } 
                    else
                    {   // 오른쪽 기울기
                        bumpscale = Math.Min(Remap(Math.Abs(fk_spine02._side), 0.0f, 70.0f, 0.0f, 1.0f), 1.0f);
                        right_spine_bs += bumpscale * 0.2f;    
                    }

                    // 목
                    if (fk_head._front > 1.0f)
                    {   // 앞으로 숙이기                
                        bumpscale = Math.Min(Remap(fk_head._front, 0.0f, 70.0f, 0.0f, 1.0f), 1.0f);
                        neck_spine_bs += bumpscale * 0.8f;
                    } else
                    {   // 뒤로 숙이기
                        bumpscale = Math.Min(Remap(Math.Abs(fk_head._front), 0.0f, 50.0f, 0.0f, 1.0f), 1.0f);
                        neckline_bs += bumpscale * 0.8f;    
                    }

                    if (fk_head._side > 1.0f)
                    {   // 왼쪽 기울기            
                        bumpscale = Math.Min(Remap(fk_head._side, 0.0f, 90.0f, 0.0f, 1.0f), 1.0f);    
                        left_neckline_bs += bumpscale * 0.4f;                                                        
                    } else
                    {   // 오른쪽 기울기
                        bumpscale = Math.Min(Remap(Math.Abs(fk_head._side), 0.0f, 90.0f, 0.0f, 1.0f), 1.0f);                 
                        right_neckline_bs += bumpscale * 0.4f;                             
                    } 
                }
                
                if (ociChar.oiCharInfo.enableIK) {          
                    if (lk_left_foot._front > 1.0f)
                    {   // 뒷방향
                        bumpscale = Math.Min(Remap(lk_left_foot._front, 0.0f, 60.0f, 0.1f, 1f), 1f);
                        left_shin_bs += bumpscale * 1.0f;
                        left_thigh_ft_bs += bumpscale * 0.2f;
                        left_butt_bs += bumpscale * 0.3f;
                        left_thigh_bk_bs += bumpscale * 0.2f;
                        left_calf_bs += bumpscale * 1.0f;
                        // TODO
                        // 발목 강조                        
                    }

                    if (lk_right_foot._front > 1.0f)
                    {   // 뒷방향         
                        bumpscale = Math.Min(Remap(lk_right_foot._front, 0.0f, 60.0f, 0.1f, 1f), 1f);
                        right_shin_bs += bumpscale * 1.0f;
                        right_thigh_ft_bs += bumpscale * 0.2f;
                        right_butt_bs += bumpscale * 0.3f;
                        right_thigh_bk_bs += bumpscale * 0.2f;
                        right_calf_bs += bumpscale * 1.0f;                                           
                        // TODO
                        // 발목 강조
                    } 

                } else if (ociChar.oiCharInfo.enableFK)
                {
                    if (fk_left_foot._front > 1.0f)
                    {   // 뒷방향       
                        float strong = CombineBySign(fk_left_knee._front, fk_left_foot._front);
                        bumpscale = Math.Min(Remap(strong, 0.0f, 60.0f, 0.1f, 1f), 1f);
                        left_shin_bs += bumpscale * 1.0f;
                        left_thigh_ft_bs += bumpscale * 0.2f;
                        left_butt_bs += bumpscale * 0.3f;
                        left_thigh_bk_bs += bumpscale * 0.2f;
                        left_calf_bs += bumpscale * 1.0f;                           
                        // TODO
                        // 발목 강조                  
                    }

                    if (fk_right_foot._front > 1.0f)
                    {   // 뒷방향      
                        float strong = CombineBySign(fk_right_knee._front, fk_right_foot._front);
                        bumpscale = Math.Min(Remap(strong, 0.0f, 60.0f, 0.1f, 1f), 1f);
                        right_shin_bs += bumpscale * 1.0f;
                        right_thigh_ft_bs += bumpscale * 0.2f;
                        right_butt_bs += bumpscale * 0.3f;
                        right_thigh_bk_bs += bumpscale * 0.2f;
                        right_calf_bs += bumpscale * 1.0f;                 
                        // TODO
                        // 발목 강조
                    }
                }                

                if (neck_spine_bs != 0.0f)
                {
                    areas.Add(InitBArea(780, 230, 60, 120, Math.Min(neck_spine_bs, 3.0f)));; 
                }
                if (neckline_bs != 0.0f)
                {   
                    areas.Add(InitBArea(260, 100, 80, 80, Math.Min(neckline_bs, 3.0f)));; // 목선    
                }
                if (left_neckline_bs != 0.0f)
                {
                    areas.Add(InitBArea(220, 100, 40, 80, Math.Min(left_neckline_bs, 3.0f)));; 
                }
                if (right_neckline_bs != 0.0f)
                {
                    areas.Add(InitBArea(300, 100, 40, 80, Math.Min(right_neckline_bs, 3.0f)));; 
                }                    
                if (spine_bs != 0.0f)
                {
                    areas.Add(InitBArea(780, 410, 60, 200, Math.Min(spine_bs, 3.0f)));; // 척추
                }
                if (ribs_bs != 0.0f)
                {   
                    areas.Add(InitBArea(250, 560, 250, 200, Math.Min(ribs_bs, 3.0f)));; // 갈비
                }

                if (left_thigh_ft_bs != 0.0f)
                {
                    areas.Add(InitBArea(400, 1000, 120, 200, Math.Min(left_thigh_ft_bs, 3.0f)));; // 앞 허벅지        
                }
                if (left_thigh_bk_bs != 0.0f)
                { 
                    areas.Add(InitBArea(660, 1000, 120, 260, Math.Min(left_thigh_bk_bs, 3.0f)));; // 뒷 허벅지                        
                }                     
                if (left_thigh_inside_bs != 0.0f)
                {
                    areas.Add(InitBArea(330, 890, 80, 160, Math.Min(left_thigh_inside_bs, 3.0f)));; // 앞 허벅지                                          
                }                     
                if (left_shin_bs != 0.0f)
                {
                    areas.Add(InitBArea(400, 1500, 180, 240, Math.Min(left_shin_bs, 3.0f)));; // 앞 정강이                                        
                }                    
                if (left_butt_bs != 0.0f)
                {
                    areas.Add(InitBArea(620, 720, 160, 240, Math.Min(left_butt_bs, 3.0f)));; // 뒷 엉덩이                                                
                }        
                if (left_calf_bs != 0.0f)
                {
                    areas.Add(InitBArea(660, 1460, 120, 160, Math.Min(left_calf_bs, 3.0f)));; // 뒷 종아리 강조                                                     
                }     
                if (left_spine_bs != 0.0f)
                {
                    areas.Add(InitBArea(100, 500, 160, 240, Math.Min(left_spine_bs, 3.0f)));;   
                }

                if (right_thigh_ft_bs != 0.0f)
                {
                    areas.Add(InitBArea(120, 1000, 120, 200, Math.Min(right_thigh_ft_bs, 3.0f)));; // 앞 허벅지                         
                }
                if (right_thigh_bk_bs != 0.0f)
                {
                    areas.Add(InitBArea(910, 1000, 120, 260, Math.Min(right_thigh_bk_bs, 3.0f)));; // 뒷쪽 허벅지
                    // UnityEngine.Debug.Log($">> right_thigh_bk_bs {right_thigh_bk_bs}");
                } 
                if (right_thigh_inside_bs != 0.0f)
                {
                    areas.Add(InitBArea(190, 890, 80, 160, Math.Min(right_thigh_inside_bs, 3.0f)));; // 앞 허벅지   
                    // UnityEngine.Debug.Log($">> right_thigh_inside_bs {right_thigh_inside_bs}");                                    
                }                    
                if (right_shin_bs != 0.0f)
                {
                    areas.Add(InitBArea(120, 1500, 180, 240, Math.Min(right_shin_bs, 3.0f)));; // 앞 정강이                                          
                    // UnityEngine.Debug.Log($">> right_shin_bs {right_shin_bs}");
                }
                if (right_butt_bs != 0.0f)
                {
                    areas.Add(InitBArea(930, 720, 160, 240, Math.Min(right_butt_bs, 3.0f)));; // 뒷쪽 엉덩이                        
                }
                if (right_calf_bs != 0.0f)
                {
                    areas.Add(InitBArea(880, 1460, 120, 160, Math.Min(right_calf_bs, 3.0f)));; // 뒷쪽 종아리 강조                                                         
                }
                if (right_spine_bs != 0.0f)
                {
                    areas.Add(InitBArea(420, 500, 160, 240, Math.Min(right_spine_bs, 3.0f)));;
                } 

                realHumanData.prev_lk_left_foot_rot = lk_left_foot._q;
                realHumanData.prev_lk_right_foot_rot = lk_right_foot._q;
                realHumanData.prev_fk_left_foot_rot = fk_left_foot._q;
                realHumanData.prev_fk_right_foot_rot = fk_right_foot._q;     
                realHumanData.prev_fk_left_knee_rot = fk_left_knee._q;
                realHumanData.prev_fk_right_knee_rot = fk_right_knee._q;
                realHumanData.prev_fk_left_thigh_rot = fk_left_thigh._q;
                realHumanData.prev_fk_right_thigh_rot = fk_right_thigh._q;
                realHumanData.prev_fk_spine01_rot = fk_spine01._q;
                realHumanData.prev_fk_spine02_rot = fk_spine02._q;
                realHumanData.prev_fk_head_rot = fk_head._q;
                realHumanData.prev_fk_left_shoulder_rot = fk_left_shoulder._q;
                realHumanData.prev_fk_right_shoulder_rot = fk_right_shoulder._q;
           
                if (origin_texture != null)
                {
                    // int kernel = _self._mergeComputeShader.FindKernel("CSMain");

                    // int w = 2048;
                    // int h = 2048;
                    // // int currentAreaCount = 0;

                    // // RenderTexture 초기화 및 재사용
                    // if (_self._body_rt == null || _self._body_rt.width != w || _self._body_rt.height != h)
                    // {
                    //     if (_self._body_rt != null) _self._body_rt.Release();
                    //     _self._body_rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    //     _self._body_rt.enableRandomWrite = true;
                    //     _self._body_rt.Create();
                    // }

                    // 영역 데이터가 변경된 경우만 업데이트
                    if (areas.Count > 0)
                    {        
                        // _self._body_areaBuffer.SetData(areas.ToArray());
                        // // 셰이더 파라미터 설정
                        // _self._mergeComputeShader.SetInt("Width", w);
                        // _self._mergeComputeShader.SetInt("Height", h);
                        // _self._mergeComputeShader.SetInt("AreaCount", areas.Count);
                        // _self._mergeComputeShader.SetTexture(kernel, "TexA", origin_texture);
                        // _self._mergeComputeShader.SetTexture(kernel, "TexB", strong_texture);
                        // _self._mergeComputeShader.SetTexture(kernel, "Result", _self._body_rt);
                        // _self._mergeComputeShader.SetBuffer(kernel, "Areas", _self._body_areaBuffer);

                        // // Dispatch 실행
                        // _self._mergeComputeShader.Dispatch(kernel, Mathf.CeilToInt(w / 8f), Mathf.CeilToInt(h / 8f), 1);                 
                        
                        // realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_name, _self._body_rt);

                        Texture2D merged =  MergeRGBAlphaMaps(origin_texture, strong_texture, areas);    
                        realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_name, merged);
                        // SaveAsPNG(merged, "./body_merge.png");
                        // SaveAsPNG(strong_texture, "./body_strong.png");
                        // SaveAsPNG(RenderTextureToTexture2D(_self._body_rt), "./body_merged.png");                     
                    }
                }
            }
            else
            {
                realHumanData.m_skin_body.SetTexture(realHumanData.body_bumpmap_name, realHumanData.bodyOriginTexture);
            }
        }
        
public static Texture2D RenderTextureToTexture2D(RenderTexture rt)
{
    if (rt == null) return null;

    // 현재 활성화된 RenderTexture 저장
    RenderTexture prev = RenderTexture.active;

    // RenderTexture 활성화
    RenderTexture.active = rt;

    // Texture2D 생성 (포맷 ARGB32 권장)
    Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);

    // 픽셀 복사
    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
    tex.Apply();

    // RenderTexture 원래대로 복원
    RenderTexture.active = prev;

    return tex;
}        
        // TODO
        // loading 시 캐릭터 추가 될때 처리
        // public TreeNodeObject AddNode(string _name, TreeNodeObject _parent = null)


        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                _self._selectedOciChar = ociChar;

                if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                {                    
                    if (realHumanData.coroutine != null)
                    {
                        ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                        ociChar.charInfo.StartCoroutine(ExecuteAfterFrame(ociChar, realHumanData));
                    }                    
                } 
                else
                {
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

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                _self._selectedOciChar = null;
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {        

                ObjectCtrlInfo objectCtrlInfo = Studio.Studio.GetCtrlInfo(_node);   
                OCIChar ociChar = objectCtrlInfo as OCIChar;

                _self._selectedOciChar = null;

                if (ociChar != null && _self._ociCharMgmt.TryGetValue(ociChar.GetHashCode(), out var realHumanData))
                {
                    if (realHumanData.coroutine != null)
                    {
                        ociChar.charInfo.StopCoroutine(realHumanData.coroutine);
                    }
                    realHumanData.c_m_eye.Clear();

                    _self._ociCharMgmt.Remove(ociChar.GetHashCode());
                }
                    
                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                ChaControl chaControl = __instance.GetChaControl();

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

                        _self._selectedOciChar = __instance;
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

    class PositionData
    {
        public Quaternion _q;
        public  float   _front;
        public  float   _side;

        public PositionData(Quaternion q, float front, float side)
        {   
            _q = q;
            _front = front;
            _side = side;
        }        
    }

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

        public Material m_skin_head;
        public Material m_skin_body;

        public List<Material> c_m_eye = new List<Material>();

        public Texture2D headOriginTexture;

        public Texture2D bodyOriginTexture;

        // public Transform tf_n_height;

        public OCIChar.BoneInfo fk_spine01_bone;

        public OCIChar.BoneInfo fk_spine02_bone;

        public OCIChar.BoneInfo fk_head_bone;

        public OCIChar.BoneInfo fk_neck_bone;

        public OCIChar.BoneInfo fk_left_shoulder_bone;

        public OCIChar.BoneInfo fk_right_shoulder_bone;

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


        public Quaternion  prev_fk_spine01_rot;
        public Quaternion  prev_fk_spine02_rot;

        public Quaternion  prev_fk_head_rot;
        public Quaternion  prev_fk_neck_rot;
        public Quaternion  prev_fk_right_shoulder_rot;
        public Quaternion prev_fk_left_shoulder_rot;
        public Quaternion  prev_fk_right_thigh_rot;
        public Quaternion  prev_fk_left_thigh_rot;
        public Quaternion  prev_fk_right_knee_rot;
        
        public Quaternion  prev_fk_left_knee_rot;        
        public Quaternion  prev_fk_right_foot_rot;
        public Quaternion  prev_fk_left_foot_rot;        
        public Quaternion  prev_lk_right_foot_rot;
        public Quaternion  prev_lk_left_foot_rot;

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

        struct BArea
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float RadiusX; // 가로 반지름
        public float RadiusY; // 세로 반지름
        public float Strong; // G/B 방향 강조
        public float BumpBooster; // 범프 세기 강조
    }


}