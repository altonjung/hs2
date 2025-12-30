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
#endif

// 추가 작업 예정
// - direction 자동 360도 회전

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
        public const string Version = "0.9.4.2";
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

        internal static Dictionary<string, string> boneDict = new Dictionary<string, string>();

        internal static ConfigEntry<bool> ConfigKeyEnableWind { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableWindShortcut { get; private set; }


        // Environment    
        internal static ConfigEntry<float> WindDirection { get; private set; }
        internal static ConfigEntry<float> WindInterval { get; private set; }
        internal static ConfigEntry<float> WindUpForce { get; private set; }
        internal static ConfigEntry<float> WindForce { get; private set; }

        internal static ConfigEntry<float> AccesoriesForce { get; private set; }
        internal static ConfigEntry<float> AccesoriesAmplitude { get; private set; }
        internal static ConfigEntry<float> AccesoriesDamping { get; private set; }
        internal static ConfigEntry<float> AccesoriesStiffness { get; private set; }

        internal static ConfigEntry<float> HairForce { get; private set; }
        internal static ConfigEntry<float> HairAmplitude { get; private set; }
        internal static ConfigEntry<float> HairDamping { get; private set; }
        internal static ConfigEntry<float> HairStiffness { get; private set; }

        internal static ConfigEntry<float> ClotheForce { get; private set; }
        internal static ConfigEntry<float> ClotheAmplitude { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffness { get; private set; }


        private static string _assemblyLocation;
        private bool _loaded = false;

        private List<ObjectCtrlInfo> _selectedOCIs = new List<ObjectCtrlInfo>();

        private float _minY = float.MaxValue;
        private float _maxY = float.MinValue;

        private bool _previousConfigKeyEnableWind;

        // 위치에 따른 바람 강도
        private AnimationCurve _heightToForceCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 위로 갈수록 약함

        private Dictionary<int, WindData> _ociObjectMgmt = new Dictionary<int, WindData>();

        private Coroutine _CheckWindActiveRoutine;

        #endregion

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();
            // Environment 
            WindDirection = Config.Bind("All", "Direction", 0f, new ConfigDescription("wind direction from 0 to 360 degree", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindUpForce = Config.Bind("All", "ForceUp", 0.0f, new ConfigDescription("wind up force", new AcceptableValueRange<float>(0.0f, 0.1f)));

            WindForce = Config.Bind("All", "Force", 0.1f, new ConfigDescription("wind force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            WindInterval = Config.Bind("All", "Interval", 2f, new ConfigDescription("wind spawn interval(sec)", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // clothes
            ClotheForce = Config.Bind("Cloth", "Force", 1.0f, new ConfigDescription("cloth force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            ClotheAmplitude = Config.Bind("Cloth", "Amplitude", 0.5f, new ConfigDescription("cloth amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothDamping = Config.Bind("Cloth", "Damping", 0.3f, new ConfigDescription("cloth damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Cloth", "Stiffness", 2.0f, new ConfigDescription("wind stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // hair
            HairForce = Config.Bind("Hair", "Force", 1.0f, new ConfigDescription("hair force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            HairAmplitude = Config.Bind("Hair", "Amplitude", 0.5f, new ConfigDescription("hair amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            HairDamping = Config.Bind("Hair", "Damping", 0.15f, new ConfigDescription("hair damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            HairStiffness = Config.Bind("Hair", "Stiffness", 0.3f, new ConfigDescription("hair stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // accesories
            AccesoriesForce = Config.Bind("Misc", "Force", 1.0f, new ConfigDescription("accesories force", new AcceptableValueRange<float>(0.1f, 1.0f)));

            AccesoriesAmplitude = Config.Bind("Misc", "Amplitude", 0.3f, new ConfigDescription("accesories amplitude", new AcceptableValueRange<float>(0.0f, 10.0f)));

            AccesoriesDamping = Config.Bind("Misc", "Damping", 0.7f, new ConfigDescription("accesories damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            AccesoriesStiffness = Config.Bind("Misc", "Stiffness", 1.0f, new ConfigDescription("accesories stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // option 
            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;


            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var harmony = HarmonyExtensions.CreateInstance(GUID);
            harmony.PatchAll(Assembly.GetExecutingAssembly());                     
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

            if (ConfigKeyEnableWindShortcut.Value.IsDown())
            {
                ConfigKeyEnableWind.Value = !ConfigKeyEnableWind.Value;
            }
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;


            _CheckWindActiveRoutine = StartCoroutine(CheckWindActiveRoutine());
        }

        private void SceneInit()
        {
            foreach (var kvp in _ociObjectMgmt)
            {
                var key = kvp.Key;
                WindData value = kvp.Value;

                value.clothes.Clear();
                value.hairDynamicBones.Clear();
                OCIChar ociChar = value.objectCtrlInfo as OCIChar;

                if (ociChar != null)
                {
                    if (value.coroutine != null)
                    {
                        ociChar.charInfo.StopCoroutine(value.coroutine);
                        value.coroutine = null;
                    }
                }
#if FEATURE_SUPPORT_ITEM
                OCIItem ociItem = value.objectCtrlInfo as OCIItem;
                if (ociItem != null)
                {
                    if (value.coroutine != null)
                    {
                        ociItem.guideObject.StopCoroutine(value.coroutine);
                        value.coroutine = null;
                    }
                }
#endif
            }
            _ociObjectMgmt.Clear();
            _selectedOCIs.Clear();
        }

        // n 개 대상 아이템에 대해 active/inactive 동시 적용 처리 
        IEnumerator CheckWindActiveRoutine()
        {
            while (true)
            {
                if(_previousConfigKeyEnableWind != ConfigKeyEnableWind.Value)
                {
                    foreach (ObjectCtrlInfo ctrlInfo in _selectedOCIs)
                    {
                        OCIChar ociChar = ctrlInfo as OCIChar;
                        if (ociChar != null && _self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        {                          
                            if (ConfigKeyEnableWind.Value)
                            {
                                OCIChar ociChar1 = windData.objectCtrlInfo as OCIChar;

                                if (windData.wind_status == Status.DESTROY)
                                {
                                    if (windData.coroutine != null)
                                    {                                       
                                        ociChar1.charInfo.StopCoroutine(windData.coroutine);
                                        windData.coroutine = null;
                                    }
                                    
                                    windData.wind_status = Status.RUN;
                                    windData.coroutine = ociChar1.charInfo.StartCoroutine(WindRoutine(windData));
                                }
                            } else
                            {
                                windData.wind_status = Status.DESTROY;
                            }                            
                        }
#if FEATURE_SUPPORT_ITEM
                        OCIItem ociItem = ctrlInfo as OCIItem;
                        if (ociItem != null && _self._ociObjectMgmt.TryGetValue(ociItem.GetHashCode(), out var windData))
                        {                          
                            if (ConfigKeyEnableWind.Value)
                            {
                                OCIItem ociItem1 = windData.objectCtrlInfo as OCIItem;

                                if (windData.wind_status == Status.DESTROY)
                                {
                                    if (windData.coroutine != null)
                                    {                                 
                                        ociItem1.guideObject.StopCoroutine(windData.coroutine);
                                        windData.coroutine = null;
                                    }
                                    
                                    windData.wind_status = Status.RUN;
                                    windData.coroutine = ociItem1.guideObject.StartCoroutine(WindRoutine(windData));
                                }
                            } else
                            {
                                windData.wind_status = Status.DESTROY;
                            }                            
                        }
#endif
                    }                        

                    _previousConfigKeyEnableWind = ConfigKeyEnableWind.Value;          
                }

                yield return new WaitForSeconds(1.0f); // 1.0초 대기
            }
        }

        void ApplyWind(Vector3 windEffect, float factor, WindData windData)
        {
            float time = Time.time;

            // factor 자체가 바람 에너지
            windEffect *= factor;

            // =========================
            // Hair
            // =========================
            foreach (var bone in windData.hairDynamicBones)
            {
                if (bone == null)
                    continue;

                float wave = Mathf.Sin(time * HairAmplitude.Value);
                if (wave < 0f) wave = 0f; // 위로만

                Vector3 finalWind = windEffect * WindForce.Value * HairForce.Value;
                finalWind.y += wave * WindUpForce.Value * factor;

                bone.m_Damping = HairDamping.Value;
                bone.m_Stiffness = HairStiffness.Value;
                bone.m_Force = finalWind;

                bone.m_Gravity = new Vector3(
                    0,
                    UnityEngine.Random.Range(-0.005f, -0.03f),
                    0
                );
            }

            // =========================
            // Accessories
            // =========================
            foreach (var bone in windData.accesoriesDynamicBones)
            {
                if (bone == null)
                    continue;

                float wave = Mathf.Sin(time * AccesoriesAmplitude.Value);
                if (wave < 0f) wave = 0f;

                Vector3 finalWind = windEffect * WindForce.Value * AccesoriesForce.Value;;
                finalWind.y += wave * WindUpForce.Value * factor;

                bone.m_Damping = AccesoriesDamping.Value;
                bone.m_Stiffness = AccesoriesStiffness.Value;
                bone.m_Force = finalWind;

                bone.m_Gravity = new Vector3(
                    0,
                    UnityEngine.Random.Range(-0.01f, -0.05f),
                    0
                );
            }

            // =========================
            // Clothes
            // =========================
            foreach (var cloth in windData.clothes)
            {
                if (cloth == null)
                    continue;

                float rawWave = Mathf.Sin(time * ClotheAmplitude.Value);

                float upWave   = Mathf.Max(rawWave, 0f);   // 올라갈 때
                float downWave = Mathf.Max(-rawWave, 0f);  // 내려올 때
                
                upWave = Mathf.SmoothStep(0f, 1f, upWave);
                downWave = Mathf.SmoothStep(0f, 1f, downWave);

                Vector3 baseWind = windEffect.normalized;

                // =========================
                // Directional (XZ)
                // =========================
                Vector3 externalWind =
                    baseWind * WindForce.Value * ClotheForce.Value;

                // =========================
                // Random + Upward
                // =========================
                float noise =
                    (Mathf.PerlinNoise(time * 0.8f, 0f) - 0.5f) * 2f;

                // 🔥 upward는 강하게
                float upBoost = 5.0f;

                // 🔻 downward는 거의 힘 주지 않음
                float downReduce = 0.15f;   // 0.1 ~ 0.3 권장

                Vector3 randomWind =
                    baseWind * noise * WindForce.Value * ClotheForce.Value +
                    Vector3.up *
                    (
                        upWave   * WindUpForce.Value * upBoost -
                        downWave * WindUpForce.Value * downReduce
                    );

                // =========================
                // Cloth physics
                // =========================
                cloth.useGravity = true;
                cloth.worldAccelerationScale = 1.0f;
                cloth.worldVelocityScale = 0.0f;

                cloth.externalAcceleration =
                    externalWind * 30f * factor * 20;

                cloth.randomAcceleration =
                    randomWind * 80f * factor * 20;

                // 🔧 하강 시 damping 증가 → elastic 제거 핵심
                float downDampingBoost = 2.0f;
                cloth.damping = ClothDamping.Value;



                cloth.stiffnessFrequency = ClothStiffness.Value;
            }
        }

        private IEnumerator FadeOutWind(WindData windData, float fadeTime)
        {
            const float step = 0.3f; // 0.3초 단위
            int steps = Mathf.CeilToInt(fadeTime / step);

            // 초기 값 저장
            var initialRandomAcceleration = new Dictionary<Cloth, Vector3>();
            var initialExternalAcceleration = new Dictionary<Cloth, Vector3>();

            foreach (var cloth in windData.clothes)
            {
                if (cloth == null) continue;

                initialRandomAcceleration[cloth] = cloth.randomAcceleration;
                initialExternalAcceleration[cloth] = cloth.externalAcceleration;
            }

            // Fade loop (Ease Out)
            for (int i = 0; i < steps; i++)
            {
                float normalized = (i + 1) / (float)steps; // 0~1
                float factor = 1f - Mathf.Pow(1f - normalized, 3); // Ease Out Cubic

                foreach (var cloth in windData.clothes)
                {
                    if (cloth == null) continue;

                    if (initialRandomAcceleration.TryGetValue(cloth, out var rnd))
                        cloth.randomAcceleration = rnd * (1f - factor);

                    if (initialExternalAcceleration.TryGetValue(cloth, out var ext))
                        cloth.externalAcceleration = ext * (1f - factor);
                }

                yield return new WaitForSeconds(step);
            }
        }

        private void ClearWind(WindData windData)
        {
            if (windData.coroutine != null)
            {
                OCIChar ociChar = windData.objectCtrlInfo as OCIChar;
                
                if (ociChar != null) {
                    ociChar.charInfo.StopCoroutine(windData.coroutine);
                }
#if FEATURE_SUPPORT_ITEM
                OCIItem ociItem = windData.objectCtrlInfo as OCIItem;
                
                if (ociItem != null) {
                    ociItem.guideObject.StopCoroutine(windData.coroutine);
                }
#endif
                windData.coroutine = null;

                windData.clothes.Clear();
                windData.hairDynamicBones.Clear();
                windData.accesoriesDynamicBones.Clear(); 
            }
        }

        private IEnumerator WindRoutine(WindData windData)
        {
            while (true)
            {
                if (_loaded == true)
                {
                    if (windData.wind_status == Status.RUN)
                    {
                        // y 위치 기반 바람세기 처리를 위한 위치 정보 획득
                        foreach (var bone in windData.hairDynamicBones)
                        {
                            if (bone == null)
                                continue;

                            float y = bone.m_Root.position.y;
                            _minY = Mathf.Min(_minY, y);
                            _maxY = Mathf.Max(_maxY, y);
                        }

                        Quaternion globalRotation = Quaternion.Euler(0f, WindDirection.Value, 0f);

                        // 방향에 랜덤성 부여 (약한 변화만 허용)
                        float angleY = UnityEngine.Random.Range(-15, 15); // 좌우 유지
                        float angleX = UnityEngine.Random.Range(-7, 7);   // 위/아래 유지 (음수면 아래 방향, 양수면 위 방향)
                        Quaternion localRotation = Quaternion.Euler(angleX, angleY, 0f);

                        Quaternion rotation = globalRotation * localRotation;

                        Vector3 direction = rotation * Vector3.back;

                        // 기본 바람 강도는 낮게 유지
                        Vector3 windEffect = direction.normalized * UnityEngine.Random.Range(0.1f, 0.15f);

                        // 적용
                        ApplyWind(windEffect, 1.0f, windData);
                        yield return new WaitForSeconds(0.3f);

                        // 자연스럽게 사라짐
                        float fadeTime = Mathf.Lerp(0.8f, 1.8f, WindForce.Value);
                        float t = 0f;
                        while (t < fadeTime)
                        {
                            t += Time.deltaTime;
                            float factor = Mathf.SmoothStep(1f, 0f, t / fadeTime); // 부드러운 감소                                
                            ApplyWind(windEffect, factor, windData);
                            yield return null;
                        }

                        // 다음 바람 전 잠깐 멈춤
                        if (WindInterval.Value > 0.1f)
                            yield return new WaitForSeconds(WindInterval.Value);

                    }
                    else if (windData.wind_status == Status.STOP || windData.wind_status == Status.DESTROY)
                    {
                        yield return StartCoroutine(FadeOutWind(windData, 0.5f));

                        if (windData.wind_status == Status.DESTROY)
                        {
                            yield break;
                        }

                        windData.wind_status = Status.IDLE;
                    }
                }

                if (windData.cloth_status == Cloth_Status.EMPTY)
                    yield return new WaitForSeconds(1);
                else
                    yield return null;
            }
        }
        #endregion

        #region Patches

        private static WindData CreateWindData(ObjectCtrlInfo ociChar)
        {
            WindData windData = new WindData();
            windData.objectCtrlInfo = ociChar;

            return windData;
        }
        private static IEnumerator ExecuteDynamicBoneAfterFrame(WindData windData)
        {
            int frameCount = 20;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            ReallocateDynamicBones(windData);
        }

        private static void StopUnselectedCtrl()
        {
            foreach (ObjectCtrlInfo ctrlInfo in _self._selectedOCIs)
            {   

                OCIChar selectedOciChar = ctrlInfo as OCIChar; 
                OCIItem selectedOciItem = ctrlInfo as OCIItem;  
                
                if (_self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData))
                {
                    if (windData.coroutine != null)
                    {
                        windData.wind_status = Status.STOP;
                    }
                }
#if FEATURE_SUPPORT_ITEM
                if (_self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData))
                {
                    if (windData.coroutine != null)
                    {
                        windData.wind_status = Status.STOP;
                    }
                }
#endif
            }

            _self._selectedOCIs.Clear();
        }

        private static void ReallocateDynamicBones(WindData windData)
        {
            windData.wind_status = ConfigKeyEnableWind.Value ? Status.RUN : Status.DESTROY;

            if (windData.wind_status == Status.RUN && windData.objectCtrlInfo != null)
            {
                OCIChar ociChar = windData.objectCtrlInfo as OCIChar;
                OCIItem ociItem = windData.objectCtrlInfo as OCIItem;

                // 기존 자원 제거
                _self.ClearWind(windData);

                if (ociChar != null) {
                    ChaControl baseCharControl = ociChar.charInfo;

                    // 신규 자원 할당
                    // Hair
                    DynamicBone[] bones = baseCharControl.objBodyBone.transform.FindLoop("cf_J_Head").GetComponentsInChildren<DynamicBone>(true);
                    windData.hairDynamicBones = bones.ToList();

                    // Accesories
                    foreach (var accessory in baseCharControl.objAccessory)
                    {
                        if (accessory != null && accessory.GetComponentsInChildren<DynamicBone>().Length > 0)
                        {
                            windData.accesoriesDynamicBones.Add(accessory.GetComponentsInChildren<DynamicBone>()[0]);
                        }
                    }

                    // Cloth
                    Cloth[] clothes = baseCharControl.transform.GetComponentsInChildren<Cloth>(true);

                    windData.clothes = clothes.ToList();
                    windData.cloth_status = windData.clothes.Count > 0 ? Cloth_Status.PHYSICS : Cloth_Status.EMPTY;
                }

                if (ociItem != null) {                    
                    DynamicBone[] bones = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<DynamicBone>(true);
                    Cloth[] clothes = ociItem.guideObject.transformTarget.gameObject.GetComponentsInChildren<Cloth>(true);

                    windData.accesoriesDynamicBones = bones.ToList();
                    windData.clothes = clothes.ToList();
                }               

                if (windData.clothes.Count != 0 || windData.hairDynamicBones.Count != 0 || windData.accesoriesDynamicBones.Count != 0)
                {
                    _self._selectedOCIs.Add(windData.objectCtrlInfo);
                    _self._ociObjectMgmt.Add(windData.objectCtrlInfo.GetHashCode(), windData);

                    // Coroutine
                    if (ociChar != null) {
                            windData.coroutine = ConfigKeyEnableWind.Value ? ociChar.charInfo.StartCoroutine(_self.WindRoutine(windData)) : null;  
                    }
#if FEATURE_SUPPORT_ITEM
                    if (ociItem != null) {
                        windData.coroutine = ConfigKeyEnableWind.Value ? ociItem.guideObject.StartCoroutine(_self.WindRoutine(windData)) : null; 
                    }
#endif
                }
            }
        }

        private static void TryAllocateObject(List<ObjectCtrlInfo> curObjCtrlInfos) {

            StopUnselectedCtrl();
            _self._selectedOCIs.Clear();

            foreach (ObjectCtrlInfo ctrlInfo in curObjCtrlInfos)
            {
                if (ctrlInfo != null)
                {
                    OCIChar ociChar = ctrlInfo as OCIChar;
                    OCIItem ociItem = ctrlInfo as OCIItem;
                    if (ociChar != null)
                    {
                        if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData1))
                        {
                            if (windData1.wind_status == Status.RUN || windData1.wind_status == Status.STOP || windData1.wind_status == Status.IDLE)
                            {
                                windData1.wind_status = Status.RUN;                            
                            } 
                            else
                            {
                                ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData1));
                            }
                        }
                        else
                        {
                            WindData windData2 = CreateWindData(ociChar);
                            ociChar.GetChaControl().StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                        }                      
                    }         

                    if (ociItem != null)
                    {
    #if FEATURE_SUPPORT_ITEM
                        if (_self._ociObjectMgmt.TryGetValue(ociItem.GetHashCode(), out var windData1))
                        {
                            if (windData1.wind_status == Status.RUN || windData1.wind_status == Status.STOP || windData1.wind_status == Status.IDLE)
                            {
                                windData1.wind_status = Status.RUN;                            
                            } 
                            else
                            {
                                ociItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData1));
                            }
                        }
                        else
                        {
                            WindData windData2 = CreateWindData(ociItem);
                            ociItem.guideObject.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData2));
                        } 
    #endif                    
                    }
                }        
            }    
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                ObjectCtrlInfo ctrlInfo = Studio.Studio.GetCtrlInfo(_node);

                List<ObjectCtrlInfo> objCtrlInfos = new List<ObjectCtrlInfo>(); 
                objCtrlInfos.Add(ctrlInfo);

                TryAllocateObject(objCtrlInfos);
             
                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {
                List<ObjectCtrlInfo> objCtrlInfos = new List<ObjectCtrlInfo>(); 
                foreach (TreeNodeObject node in Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes)
                {
                    ObjectCtrlInfo ctrlInfo = Studio.Studio.GetCtrlInfo(node);
                    objCtrlInfos.Add(ctrlInfo);                  
                }

                TryAllocateObject(objCtrlInfos); 

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
                    if (ociChar != null && _self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData1))
                    {
                        if (windData1.coroutine != null)
                        {
                            windData1.wind_status = Status.STOP;
                        }
                    }
#if FEATURE_SUPPORT_ITEM
                    OCIItem ociItem = ctrlInfo as OCIItem;
                    if (ociItem != null && _self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData2))
                    {
                        if (windData2.coroutine != null)
                        {
                            windData2.wind_status = Status.STOP;
                        }
                    }
#endif
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

                    if (ociChar != null) {
                        if (_self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData))
                        {
                            if (windData.coroutine != null)
                            {
                                windData.wind_status = Status.DESTROY;                            
                            }
                        }
                        _self._ociObjectMgmt.Remove(ociChar.GetHashCode());
                    }
#if FEATURE_SUPPORT_ITEM
                    OCIItem ociItem = ctrlInfo as OCIItem;

                    if (ociItem != null) {
                        if (_self._ociObjectMgmt.TryGetValue(ctrlInfo.GetHashCode(), out var windData))
                        {
                            if (windData.coroutine != null)
                            {
                                windData.wind_status = Status.DESTROY;                     
                            }
                        }
                        _self._ociObjectMgmt.Remove(ociItem.GetHashCode());
                    }             
#endif       
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

                if (chaControl != null)
                {
                    if (_self._ociObjectMgmt.TryGetValue(chaControl.GetHashCode(), out var windData))
                        chaControl.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        // 개별 옷 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        // 코디네이션 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        // 악세러리 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeAccessory", typeof(int), typeof(int), typeof(int), typeof(string), typeof(bool))]
        private static class ChaControl_ChangeAccessory_Patches
        {
            private static void Postfix(ChaControl __instance, int slotNo, int type, int id, string parentKey, bool forceChange)
            {
                OCIChar ociChar = __instance.GetOCIChar() as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociObjectMgmt.TryGetValue(ociChar.GetHashCode(), out var windData))
                        __instance.StartCoroutine(ExecuteDynamicBoneAfterFrame(windData));
                }
            }
        }

        //[HarmonyPatch(typeof(OCIItem), "OnVisible", typeof(bool))]
        //private static class OCIItem_OnVisible_Patches
        //{
        //    private static void Postfix(OCIItem __instance, bool _visible)
        //    {                
        //        OCIItem item = __instance as OCIItem;
        //        UnityEngine.Debug.Log($">> OCIItem OnVisible {item.guideObject.name}");

        //    }
        //}

        //[HarmonyPatch(typeof(OCIItem), "OnDelete")]
        //private static class OCIItem_OnDelete_Patches
        //{
        //    private static void Postfix(OCIItem __instance)
        //    {                
        //        // OCIItem item = _child as OCIItem;
        //        UnityEngine.Debug.Log($">> OCIItem OnDelete");
        //    }
        //}   

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                // UnityEngine.Debug.Log($">> InitScene");
                _self.SceneInit();
                return true;
            }
        }

        #endregion
    }   
    
    enum Cloth_Status
    {
        PHYSICS,
        EMPTY
    }

    enum Status
    {
        RUN,
        STOP,
        DESTROY,
        IDLE
    }


    class WindData
    {
        public ObjectCtrlInfo objectCtrlInfo;

        public Coroutine coroutine;
        public List<Cloth> clothes = new List<Cloth>();        

        public List<DynamicBone> hairDynamicBones = new List<DynamicBone>();

        public List<DynamicBone> accesoriesDynamicBones = new List<DynamicBone>();

        public SkinnedMeshRenderer clothTopRender;

        public SkinnedMeshRenderer clothBottomRender;

        public SkinnedMeshRenderer hairRender;

        public SkinnedMeshRenderer headRender;

        public SkinnedMeshRenderer bodyRender;

        public Status wind_status = Status.IDLE;

        public Cloth_Status cloth_status = Cloth_Status.EMPTY;

        public WindData()
        {
        }
    }

}