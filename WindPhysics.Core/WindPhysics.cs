using Studio;
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
        public const string Version = "0.9.4";
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

        internal static ConfigEntry<bool> ConfigKeyHairDown { get; private set; }

        internal static ConfigEntry<bool> ConfigKeyEnableWind { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableWindShortcut { get; private set; }
        internal static ConfigEntry<KeyboardShortcut> ConfigKeyRefreshWindShortcut { get; private set; }


        // Environment    
        internal static ConfigEntry<float> WindDirection { get; private set; }
        internal static ConfigEntry<float> WindInterval { get; private set; }
        internal static ConfigEntry<float> WindBase { get; private set; }


        internal static ConfigEntry<float> WindUpward { get; private set; }
        internal static ConfigEntry<float> WindHairStrong { get; private set; }
        internal static ConfigEntry<float> WindHairFrequency { get; private set; }
        internal static ConfigEntry<float> HairDamping { get; private set; }
        internal static ConfigEntry<float> HairStiffnessFrequency { get; private set; }


        internal static ConfigEntry<float> WindClotheStrong { get; private set; }
        internal static ConfigEntry<float> WindClotheFrequency { get; private set; }
        internal static ConfigEntry<float> ClothDamping { get; private set; }
        internal static ConfigEntry<float> ClothStiffnessFrequency { get; private set; }


        private static string _assemblyLocation;
        private bool _loaded = false;
        private Status _status = Status.IDLE;
        private ObjectCtrlInfo _selectedOCI;

        private float _minY = float.MaxValue;
        private float _maxY = float.MinValue;

        // 위치에 따른 바람 강도
        private AnimationCurve _heightToForceCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.1f); // 위로 갈수록 약함


        private Coroutine _windCoroutine;

        private List<DynamicBone> _dynamicBones = new List<DynamicBone>();
        private List<Cloth> _clothes = new List<Cloth>();

        internal enum Status
        {
            RUN,
            STOP,
            DESTROY,
            IDLE
        }

        #endregion

        #region Accessors
        internal static ConfigEntry<KeyboardShortcut> ConfigMainWindowShortcut { get; private set; }
        #endregion


        #region Unity Methods
        protected override void Awake()
        {
            base.Awake();

            ConfigKeyHairDown = Config.Bind("Options", "Hair down", false, "Wind enabled/disabled");

            ConfigKeyEnableWind = Config.Bind("Options", "Toggle effect", false, "Wind enabled/disabled");

            ConfigKeyEnableWindShortcut = Config.Bind("ShortKey", "Toggle effect key", new KeyboardShortcut(KeyCode.W));

            ConfigKeyRefreshWindShortcut = Config.Bind("ShortKey", "Refresh effect", new KeyboardShortcut(KeyCode.R));

            // 
            WindDirection = Config.Bind("Common", "Direction", 0f, new ConfigDescription("Set wind direction", new AcceptableValueRange<float>(0.0f, 359.0f)));

            WindInterval = Config.Bind("Common", "Interval", 2f, new ConfigDescription("Set time interval. ms to sec", new AcceptableValueRange<float>(0.0f, 10.0f)));

            WindBase = Config.Bind("Common", "Base", 0.7f, new ConfigDescription("Set base speed. higher value is more faster", new AcceptableValueRange<float>(0.1f, 5.0f)));

            WindUpward = Config.Bind("Common", "Upward", 0.1f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 5.0f)));

            WindHairStrong = Config.Bind("_Hair", "Strong", 1f, new ConfigDescription("Set wind strong. higher value is more strong", new AcceptableValueRange<float>(0.1f, 10.0f)));

            WindHairFrequency = Config.Bind("_Hair", "Frequency", 0.5f, new ConfigDescription("Set wind frequency. higher value is more frequency", new AcceptableValueRange<float>(0.0f, 10.0f)));

            HairDamping = Config.Bind("_Hair", "Damping", 0.15f, new ConfigDescription("Set hair damping. higher value is more damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            HairStiffnessFrequency = Config.Bind("_Hair", "Stiffness", 1.0f, new ConfigDescription("Set hair stiffness. higher value is more stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            //
            WindClotheStrong = Config.Bind("_Cloth", "Strong", 100f, new ConfigDescription("Set clothe wind strong. higher value is more strong", new AcceptableValueRange<float>(0.0f, 500.0f)));

            WindClotheFrequency = Config.Bind("_Cloth", "Frequency", 0.5f, new ConfigDescription("Set clothe wind frequency. higher value is more frequency", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothDamping = Config.Bind("_Cloth", "Damping", 0.15f, new ConfigDescription("Set hair damping. higher value is more damping", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffnessFrequency = Config.Bind("_Cloth", "Stiffness", 1.0f, new ConfigDescription("Set hair stiffness. higher value is more stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));


            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);


            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

            var harmony = HarmonyExtensions.CreateInstance(GUID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            _windCoroutine = StartCoroutine(WindRoutine());
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

            if (ConfigKeyEnableWind.Value)
            {
                if (_selectedOCI != null)
                    _status = Status.RUN;
                else
                    _status = Status.IDLE;
            }
            else
            {
                if (_status == Status.RUN)
                    _status = Status.STOP;
                else 
                    _status = Status.IDLE;                
            }

            if (ConfigKeyRefreshWindShortcut.Value.IsDown())
            {
                if (_status == Status.IDLE)
                    RefreshDynamicBones(_selectedOCI);
                else
                    AllocateDynamicBones(_selectedOCI);
            }
        }


        private void OnSceneLoad(string path)
        {
            _status = Status.STOP;
            _selectedOCI = null;
        }

        #endregion

        #region Public Methods

        #endregion

        #region Private Methods
        private void Init()
        {
            UIUtility.Init();
            _loaded = true;
        }

        void ApplyWind(Vector3 windBase, float factor) // 기본 바람 방향
        {

            windBase = windBase * factor;

            float time = Time.time;

            foreach (var bone in _dynamicBones)
            {
                if (bone == null)
                    continue;

                float height = bone.m_Root.position.y;

                float normalizedHeight = Mathf.InverseLerp(_minY, _maxY, height);
                float heightFactor = _heightToForceCurve.Evaluate(normalizedHeight);
                float timeWave = Mathf.Sin(time * WindHairFrequency.Value + height) * WindUpward.Value; // 바람의 위 아래 폭 움직임 주기
                float timeFactor = Mathf.Lerp(0.1f, WindHairStrong.Value, timeWave); // 바람의 강약 조절 

                Vector3 adjustedWind = windBase * heightFactor * timeFactor;
                bone.m_Damping = HairDamping.Value;
                bone.m_Stiffness = HairStiffnessFrequency.Value;
                bone.m_Force = adjustedWind;
            }

            time = Time.time;
            foreach (var cloth in _clothes)
            {
                if (cloth == null)
                    continue;

                float offset = Random.Range(0f, Mathf.PI * 2f);
                float strength = Random.Range(0.5f, 1.5f);

                // 위/아래 출렁임 (Sine 함수 기반) -> 위는 강하게, 아래는 약하게 움직임..
                float sin = Mathf.Sin(time * WindClotheFrequency.Value + offset);
                float verticalWave = sin * (sin >= 0f ? WindUpward.Value : 0.1f) * strength;

                // 좌/우 흔들림 (PerlinNoise 기반)
                float amplitude = Random.Range(0.1f, 0.5f); // 좌우 진폭 크기 (값이 작을수록 진동이 느리게...)
                float horizontalShake = (Mathf.PerlinNoise(time * amplitude, 0f) - 0.5f) * 2.0f * 1.5f; // X축 (좌우)        

                Vector3 externalWind = windBase + new Vector3(0f, verticalWave, 0f);

                Vector3 randomWind = new Vector3(horizontalShake, verticalWave * 0.3f, 0f);
                cloth.useGravity = true;
                cloth.worldAccelerationScale = 0.5f; // 외부 가속도 반영 비율
                cloth.worldVelocityScale = 0.5f;
                cloth.randomAcceleration = randomWind * WindClotheStrong.Value;
                cloth.damping = ClothDamping.Value;
                cloth.stiffnessFrequency = ClothStiffnessFrequency.Value;
                cloth.externalAcceleration = externalWind.normalized * WindClotheStrong.Value;
            }
        }

        private IEnumerator FadeOutWind(float fadeTime)
        {
            float t = 0f;

            // 현재 값을 저장
            var initialRandomAcceleration = new Dictionary<Cloth, Vector3>();
            var initialExternalAcceleration = new Dictionary<Cloth, Vector3>();
            var initialWorldAccelScale = new Dictionary<Cloth, float>();
            var initialWorldVelocityScale = new Dictionary<Cloth, float>();

            foreach (var cloth in _clothes)
            {
                if (cloth == null) continue;

                initialRandomAcceleration[cloth] = cloth.randomAcceleration;
                initialExternalAcceleration[cloth] = cloth.externalAcceleration;
                initialWorldAccelScale[cloth] = cloth.worldAccelerationScale;
                initialWorldVelocityScale[cloth] = cloth.worldVelocityScale;
            }

            // Fade loop
            while (t < fadeTime)
            {
                t += Time.deltaTime;

                // Linear로 감소 (체감 시간 보장)
                float normalized = Mathf.Clamp01(t / fadeTime);
                float factor = 1f - normalized;

                foreach (var cloth in _clothes)
                {
                    if (cloth == null) continue;

                    cloth.randomAcceleration = initialRandomAcceleration[cloth] * factor;
                    cloth.externalAcceleration = initialExternalAcceleration[cloth] * factor;
                    cloth.worldAccelerationScale = initialWorldAccelScale[cloth] * factor;
                    cloth.worldVelocityScale = initialWorldVelocityScale[cloth] * factor;
                }

                yield return null;
            }
        }
        
        private void ClearWind()
        {
            _clothes.Clear();
            _dynamicBones.Clear();
        }

        private IEnumerator WindRoutine()
        {
            while (true)
            {
                if (_loaded == true)
                {
                    if (_status == Status.RUN)
                    {
                        // y 위치 기반 바람세기 처리를 위한 위치 정보 획득
                        foreach (var bone in _dynamicBones)
                        {
                            if (bone == null)
                                continue;

                            float y = bone.m_Root.position.y;
                            _minY = Mathf.Min(_minY, y);
                            _maxY = Mathf.Max(_maxY, y);
                        }

                        Quaternion globalRotation = Quaternion.Euler(0f, WindDirection.Value, 0f);

                        // 방향에 랜덤성 부여 (약한 변화만 허용)
                        float angleY = Random.Range(-10, 10); // 좌우 유지
                        float angleX = Random.Range(-5, 5);   // 위/아래 유지 (음수면 아래 방향, 양수면 위 방향)
                        Quaternion localRotation = Quaternion.Euler(angleX, angleY, 0f);

                        Quaternion rotation = globalRotation * localRotation;

                        Vector3 direction = rotation * Vector3.back;

                        // 기본 바람 강도는 낮게 유지
                        Vector3 windDirection = direction.normalized * Random.Range(0.01f, WindBase.Value);

                        // 적용
                        ApplyWind(windDirection, 1f);
                        yield return new WaitForSeconds(0.5f);

                        // 자연스럽게 사라짐
                        float fadeTime = Random.Range(0.3f, 1.5f);
                        float t = 0f;
                        while (t < fadeTime)
                        {
                            t += Time.deltaTime;
                            float factor = Mathf.SmoothStep(1f, 0f, t / fadeTime); // 부드러운 감소
                            ApplyWind(windDirection, factor);
                            yield return null;
                        }

                        // 다음 바람 전 잠깐 멈춤
                        if (WindInterval.Value > 0.1f)
                            yield return new WaitForSeconds(Random.Range(WindInterval.Value, WindInterval.Value * 2));

                    }
                    else if (_status == Status.STOP || _status == Status.DESTROY)
                    {
                        yield return StartCoroutine(FadeOutWind(7.0f));

                        // 한번 더 pulldown 처리
                        RefreshDynamicBones(_selectedOCI);

                        if (_status == Status.DESTROY)
                        {
                            ClearWind(); // 최종 정리
                        }
                        
                         _status = Status.IDLE;
                    }
                }

                yield return null;
            }
        }
        #endregion

        #region Patches

        private static IEnumerator ExecuteAfterFrame(ObjectCtrlInfo objectCtrlInfo)
        {
            int frameCount = 5;
            for (int i = 0; i < frameCount; i++)
                yield return null;
            
            AllocateDynamicBones(objectCtrlInfo);
        }

        private static void RefreshDynamicBones(ObjectCtrlInfo objectCtrlInfo)
        {
            if (objectCtrlInfo != null)
            {
                foreach (var cloth in _self._clothes)
                {
                    if (cloth == null)
                        continue;
                    cloth.damping = ClothDamping.Value;
                    cloth.stiffnessFrequency = ClothStiffnessFrequency.Value;
                    cloth.externalAcceleration = Vector3.down * 5f;
                }               
            }
        }
        

        private static void AllocateDynamicBones(ObjectCtrlInfo objectCtrlInfo)
        {
            if (objectCtrlInfo != null)
            {
                _self._selectedOCI = objectCtrlInfo;

                Cloth[] cloths = _self._selectedOCI.guideObject.transformTarget.GetComponentsInChildren<Cloth>(true);
                DynamicBone[] bones = _self._selectedOCI.guideObject.transformTarget.GetComponentsInChildren<DynamicBone>(true);

                _self._clothes.Clear();
                foreach (var cloth in cloths)
                {
                    if (cloth == null || cloth.transform == null)
                        continue;

                    _self._clothes.Add(cloth);
                }

                // dynamic bone (머리카락)에 대해 기본적인 gravity를 모두 자동 적용
                _self._dynamicBones.Clear();
                foreach (var bone in bones)
                {
                    if (bone == null || bone.m_Root == null)
                        continue;

                    if (!bone.gameObject.name.Contains("Belly") && !bone.gameObject.name.Contains("Vagina") && !bone.gameObject.name.Contains("Ana") && !bone.gameObject.name.Contains("Leg"))
                    {
#if FEATURE_AUTO_HAIR_GRAVITY
                        if (ConfigKeyHairDown.Value == true) {
                            if (bone.m_Gravity.y == 0f)
                            {
                                Vector3 gravity = bone.m_Gravity;
                                gravity.y = -0.01f;
                                bone.m_Gravity = gravity;
                            }

                            if (bone.m_Force.y == 0f)
                            {
                                Vector3 force = bone.m_Force;
                                force.y = -0.05f;
                                bone.m_Force = force;
                            }
                        }
#endif
                        _self._dynamicBones.Add(bone);
                    }
                }
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
                    AllocateDynamicBones(objectCtrlInfo);
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        private static class WorkspaceCtrl_OnSelectMultiple_Patches
        {
            private static bool Prefix(object __instance)
            {

                TreeNodeObject _node = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes[0];

                ObjectCtrlInfo objectCtrlInfo = null;

                if (Singleton<Studio.Studio>.Instance.dicInfo.TryGetValue(_node, out objectCtrlInfo))
                {
                    AllocateDynamicBones(objectCtrlInfo);
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
                {
                    _self._status = Status.DESTROY;
                    _self._selectedOCI = null;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                AllocateDynamicBones(__instance as ObjectCtrlInfo);
            }
        }

        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.ChangeCustomClothes), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]        
        internal static class ChaControl_ChangeCustomClothes_Patches
        {
            public static void Postfix(ChaControl __instance, int kind, bool updateColor, bool updateTex01, bool updateTex02, bool updateTex03)
            {
                // UnityEngine.Debug.Log($">> ChangeCustomClothes {kind}");
                if (__instance != null)
                {
                    __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar() as ObjectCtrlInfo));
                }
            }           
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self._status = Status.DESTROY;
                _self._selectedOCI = null;

                return true;
            }
        }

        #endregion
    }
}
