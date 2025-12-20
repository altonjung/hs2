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
using UnityEngine.Rendering;
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
using ADV.Commands.Camera;
using KKAPI.Studio;
using System;
#endif

namespace ClothPhysics
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class ClothPhysics : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothPhysics";
        public const string Version = "0.9.0";
        public const string GUID = "com.alton.illusionplugins.clothphysics";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_physics";
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
        internal static ClothPhysics _self;

        internal static Dictionary<string, string> boneDict = new Dictionary<string, string>();

        // internal static ConfigEntry<bool> ConfigKeyEnableTight { get; private set; }

        // internal static ConfigEntry<float> ClothTightStrength { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistanceTop { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistanceMiddle { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistanceBottom { get; private set; }

        internal static ConfigEntry<float> ClothAccBottom { get; private set; }

        internal static ConfigEntry<float> ClothDamping { get; private set; }

        internal static ConfigEntry<float> ClothStiffness { get; private set; }


        internal static ConfigEntry<KeyboardShortcut> ConfigKeyEnableUndressShortcut { get; private set; }

        // internal static ConfigEntry<KeyboardShortcut> ConfigKeyPrintShortcut { get; private set; }

        // Test
        internal static ConfigEntry<float> ClothUndressDuration { get; private set; }

        internal static ConfigEntry<float> ClothMaxDistance { get; private set; }


        private static string _assemblyLocation;
        private bool _loaded = false;
        private Status _status = Status.IDLE;
        private ObjectCtrlInfo _selectedOCI;

        private Dictionary<Cloth, float[]> _originalMaxDistances = new Dictionary<Cloth, float[]>();

        private Dictionary<TreeNodeObject, OCICollider> clothesColliders = new Dictionary<TreeNodeObject, OCICollider>();

        private Transform _pelvisBone;

        private Coroutine _clothCoroutine;

        private List<Cloth> _clothes = new List<Cloth>();

        private SkinnedMeshRenderer _selectedOciSmr;

        private TreeNodeObject _collider_folder_component;


        internal enum Status
        {
            TIGHT,
            UNDRESS,
            DESTORY,
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

            // ConfigKeyEnableTight = Config.Bind("Tight", "Tight", false, "Cloth tight enabled/disabled");

            // ClothTightStrength = Config.Bind("Tight", "Strength", 1.0f, new ConfigDescription("Set cloth stiffness. higher value is more stiffness", new AcceptableValueRange<float>(-300.0f, 300.0f)));

            ClothMaxDistanceTop = Config.Bind("Undress", "Top", 3.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothMaxDistanceMiddle = Config.Bind("Undress", "Middle", 8.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothMaxDistanceBottom = Config.Bind("Undress", "Bottom", 10.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));

            ClothAccBottom = Config.Bind("Undress", "Acc", 30.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(30.0f, 300.0f)));

            ClothDamping = Config.Bind("Undress", "Damping", 0.5f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 1.0f)));

            ClothStiffness = Config.Bind("Undress", "Stiffness", 1.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 10.0f)));


            ConfigKeyEnableUndressShortcut = Config.Bind("ShortKey", "Undress key", new KeyboardShortcut(KeyCode.U));

            // ClothPullDown = Config.Bind("Undress", "Pulldown", 1.0f, new ConfigDescription("Blow wind upward. higher value is more upward", new AcceptableValueRange<float>(0.0f, 300.0f)));

            ClothUndressDuration = Config.Bind("Undress", "Duration", 10.0f, new ConfigDescription("Set undress duration", new AcceptableValueRange<float>(0.0f, 90.0f)));

            ClothMaxDistance = Config.Bind("Undress", "Distance", 0.01f, new ConfigDescription("Set cloth stiffness. higher value is more stiffness", new AcceptableValueRange<float>(0.0f, 10.0f)));

            // ConfigKeyPrintShortcut = Config.Bind("ShortKey", "Print clothes", new KeyboardShortcut(KeyCode.LeftControl, KeyCode.P));


            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);


            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

            var harmony = HarmonyExtensions.CreateInstance(GUID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());

            _clothCoroutine = StartCoroutine(ClothRoutine());
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

            if (ConfigKeyEnableUndressShortcut.Value.IsDown())
            {
                if (_selectedOCI != null)
                    _status = Status.UNDRESS;
                else
                    _status = Status.IDLE;
            }

            // if (ConfigKeyPrintShortcut.Value.IsDown())
            // {
            //     GetAllPhysicsClothes();
            // }

            // if (ConfigKeyEnableTight.Value)
            // { 
            //     if (_selectedOCI != null)
            //         _status = Status.TIGHT;
            //     else
            //         _status = Status.IDLE;                
            // }
            // else
            // {
            //     if (_status == Status.TIGHT)
            //         _status = Status.DESTORY;
            //     else
            //         _status = Status.IDLE;
            // }          
        }


        private void OnSceneLoad(string path)
        {
            _status = Status.DESTORY;
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

        void RestoreMaxDistances()
        {
            if (_selectedOciSmr == null) return;

            Mesh mesh = _selectedOciSmr.sharedMesh;
            if (mesh == null) return;

            foreach (var cloth in _clothes)
            {
                if (cloth == null) continue;

                float[] originalMax = _originalMaxDistances[cloth];

                if (originalMax.Length > 0)
                {
                    Vector3[] vertices = mesh.vertices;
                    ClothSkinningCoefficient[] coeffs = cloth.coefficients;

                    int vertexCount = Mathf.Min(vertices.Length, coeffs.Length);

                    for (int i = 0; i < vertexCount; i++)
                    {
                        coeffs[i].maxDistance = originalMax[i];
                    }
                    cloth.coefficients = coeffs;
                }
                cloth.useGravity = true;
                cloth.externalAcceleration = Vector3.zero;
            }
        }

        private IEnumerator UndressClothSegmented(
            Cloth cloth,
            float[] startDistances,
            float duration,
            float topMaxDistance,
            float midMaxDistance,
            float bottomMaxDistance)
        {
            var coeffs = cloth.coefficients;

            float timer = 0f;
            while (timer < duration)
            {
                float t = timer / duration;
                float tSmooth = Mathf.SmoothStep(1f, 1f, t); // 부드럽게 1 → 2 보간

                // 부위별 속도/배율 차이
                float topScale = Mathf.Lerp(1f, 2f, t); // 상단은 천천히
                float midScale = Mathf.Lerp(1f, 1f, t); // 허리 부근은 더 천천히
                float bottomScale = Mathf.Lerp(1f, 5f, t); // 하단은 빠르게

                for (int i = 0; i < coeffs.Length; i++)
                {
                    float normalized = (float)i / (coeffs.Length - 1);
                    float targetMaxDistance;

                    if (normalized < 0.80f) // 상단
                        targetMaxDistance = Mathf.Lerp(startDistances[i], topMaxDistance * topScale * tSmooth, t);
                    else if (normalized < 0.90f) // 허리
                        targetMaxDistance = Mathf.Lerp(startDistances[i], midMaxDistance * midScale * tSmooth, t);
                    else // 하단
                        targetMaxDistance = Mathf.Lerp(startDistances[i], bottomMaxDistance * bottomScale * tSmooth, t);

                    coeffs[i].maxDistance = targetMaxDistance;
                }

                cloth.coefficients = coeffs;

                timer += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator UndressAllBasic(float duration, float topMaxDistance, float middleMaxDistance, float bottomMaxDistance, float externalAcc)
        {
            foreach (var cloth in _clothes)
            {
                if (cloth == null) continue;

                // 물리 안정화
                cloth.damping = ClothDamping.Value;
                cloth.stiffnessFrequency = ClothStiffness.Value;

                // UNDRESS용 아래로 당기는 힘 적용
                cloth.externalAcceleration = new Vector3(0, -1, -0.1f) * externalAcc;

                // MaxDistance 초기값 가져오기
                var coeffs = cloth.coefficients;
                float[] startDistances = new float[coeffs.Length];
                for (int i = 0; i < coeffs.Length; i++)
                    startDistances[i] = coeffs[i].maxDistance;

                yield return StartCoroutine(UndressClothSegmented(cloth, startDistances, duration, topMaxDistance, middleMaxDistance, bottomMaxDistance));
            }
        }

        private IEnumerator ClothRoutine()
        {
            while (true)
            {
                if (_loaded == true)
                {
                    // if (_status == Status.TIGHT)
                    // {
                    //     ApplyTighteningAll();
                    //     yield return new WaitForSeconds(1.0f);
                    // }
                    // else
                    if (_status == Status.UNDRESS)
                    {
                        yield return StartCoroutine(UndressAllBasic(ClothUndressDuration.Value, ClothMaxDistanceTop.Value, ClothMaxDistanceMiddle.Value, ClothMaxDistanceBottom.Value, ClothAccBottom.Value));
                        RestoreMaxDistances();

                        _status = Status.IDLE;
                    }
                    else if (_status == Status.DESTORY)
                    {
                        _status = Status.IDLE;
                        // 원복                    
                        RestoreMaxDistances();
                        // 제거
                        _clothes.Clear();
                        _originalMaxDistances.Clear();
                    }
                }

                yield return null;
            }
        }
        #endregion

        #region Patches

        private static Transform GetPelvisBone(SkinnedMeshRenderer smr)
        {
            return smr.rootBone;
        }

        private static SkinnedMeshRenderer GetBodyRenderer(Transform targetTransform)
        {
            SkinnedMeshRenderer bodyRenderer = null;
#if AISHOUJO || HONEYSELECT2
            List<Transform> transformStack = new List<Transform>();

            transformStack.Add(targetTransform);

            while (transformStack.Count != 0)
            {
                Transform currTransform = transformStack[transformStack.Count - 1];
                transformStack.RemoveAt(transformStack.Count - 1);

                if (currTransform.Find("p_cf_body_00"))
                {
                    Transform bodyTransform = currTransform.Find("p_cf_body_00");
                    AIChara.CmpBody bodyCmp = bodyTransform.GetComponent<AIChara.CmpBody>();

                    if (bodyCmp != null)
                    {
                        if (bodyCmp.targetCustom != null && bodyCmp.targetCustom.rendBody != null)
                        {
                            bodyRenderer = bodyCmp.targetCustom.rendBody.transform.GetComponent<SkinnedMeshRenderer>();
                        }
                        else
                        {
                            if (bodyCmp.targetEtc != null && bodyCmp.targetEtc.objBody != null)
                            {
                                bodyRenderer = bodyCmp.targetEtc.objBody.GetComponent<SkinnedMeshRenderer>();
                            }
                        }
                    }

                    break;
                }
                else if(currTransform.Find("p_cm_body_00"))
                {
                    Transform bodyTransform = currTransform.Find("p_cm_body_00");
                    AIChara.CmpBody bodyCmp = bodyTransform.GetComponent<AIChara.CmpBody>();

                    if (bodyCmp != null)
                    {
                        if (bodyCmp.targetCustom != null && bodyCmp.targetCustom.rendBody != null)
                        {
                            bodyRenderer = bodyCmp.targetCustom.rendBody.transform.GetComponent<SkinnedMeshRenderer>();
                        }
                        else
                        {
                            if (bodyCmp.targetEtc != null && bodyCmp.targetEtc.objBody != null)
                            {
                                bodyRenderer = bodyCmp.targetEtc.objBody.GetComponent<SkinnedMeshRenderer>();
                            }
                        }
                    }

                    break;
                }

                for (int i = 0; i < currTransform.childCount; i++)
                {
                    transformStack.Add(currTransform.GetChild(i));
                }
            }
#endif
            return bodyRenderer;
        }

        private static Dictionary<Collider, List<Renderer>> debugRenderers = new Dictionary<Collider, List<Renderer>>();

        // CapsuleCollider 디버그
        private static void CreateCapsuleDebugWithName(CapsuleCollider capsule, string name, List<GameObject> debugObjects)
        {
            Camera cam = Camera.main;

            GameObject root = new GameObject(capsule.name + "_CapsuleDebug");
            root.transform.SetParent(capsule.transform, false);
            root.transform.localPosition = capsule.center;
            root.transform.localRotation = Quaternion.identity;

            List<Renderer> renderers = new List<Renderer>();

            // Top Sphere
            GameObject topSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            topSphere.transform.SetParent(root.transform, false);
            topSphere.transform.localPosition = Vector3.up * (capsule.height * 0.5f - capsule.radius);
            topSphere.transform.localScale = Vector3.one * capsule.radius * 2f;
            DebugMatUtil.SetTransparent(topSphere.GetComponent<Renderer>(), new Color(0f, 1f, 0f, 0.5f));
            Destroy(topSphere.GetComponent<Collider>());
            renderers.Add(topSphere.GetComponent<Renderer>());
            debugObjects.Add(topSphere);

            // Bottom Sphere
            GameObject bottomSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bottomSphere.transform.SetParent(root.transform, false);
            bottomSphere.transform.localPosition = Vector3.down * (capsule.height * 0.5f - capsule.radius);
            bottomSphere.transform.localScale = Vector3.one * capsule.radius * 2f;
            DebugMatUtil.SetTransparent(bottomSphere.GetComponent<Renderer>(), new Color(0f, 1f, 0f, 0.5f));
            Destroy(bottomSphere.GetComponent<Collider>());
            renderers.Add(bottomSphere.GetComponent<Renderer>());
            debugObjects.Add(bottomSphere);

            // Body (Cylinder)
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(capsule.radius * 2f, capsule.height * 0.5f - capsule.radius, capsule.radius * 2f);
            DebugMatUtil.SetTransparent(body.GetComponent<Renderer>(), new Color(0f, 1f, 0.5f, 0.5f));
            Destroy(body.GetComponent<Collider>());
            renderers.Add(body.GetComponent<Renderer>());
            debugObjects.Add(body);

            debugRenderers[capsule] = renderers;

            // 텍스트
            Vector3 textPos = Vector3.up * (capsule.height * 0.5f + 0.1f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects);
        }

        // BoxCollider 디버그
        private static void CreateBoxDebugWithName(BoxCollider box, string name, List<GameObject> debugObjects)
        {
            Camera cam = Camera.main;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(box.transform, false);
            go.transform.localPosition = box.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = box.size;

            DebugMatUtil.SetTransparent(go.GetComponent<Renderer>(), new Color(0.5f, 1f, 0f, 0.5f));
            Destroy(go.GetComponent<Collider>());

            debugRenderers[box] = new List<Renderer> { go.GetComponent<Renderer>() };
            debugObjects.Add(go);

            Vector3 textPos = box.center + Vector3.up * (Mathf.Max(box.size.x, box.size.y, box.size.z) * 0.5f + 0.1f);
            CreateTextDebugLocal(box.transform, textPos, name, cam, debugObjects);
        }

        // SphereCollider 디버그
        private static void CreateSphereDebugWithName(SphereCollider sphere, string name, List<GameObject> debugObjects)
        {
            Camera cam = Camera.main;
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.transform.SetParent(sphere.transform, false);
            go.transform.localPosition = sphere.center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * sphere.radius * 2f;

            DebugMatUtil.SetTransparent(go.GetComponent<Renderer>(), new Color(0f, 1f, 0f, 0.5f));
            Destroy(go.GetComponent<Collider>());

            debugRenderers[sphere] = new List<Renderer> { go.GetComponent<Renderer>() };
            debugObjects.Add(go);

            Vector3 textPos = sphere.center + Vector3.up * (sphere.radius + 0.05f);
            CreateTextDebugLocal(sphere.transform, textPos, name, cam, debugObjects);
        }

        // Text 도 bone 기준으로 움직이도록 수정
        private static void CreateTextDebugLocal(Transform parent, Vector3 localPos, string text, Camera cam, List<GameObject> debugObjects)
        {
            GameObject textObj = new GameObject("ColliderName");
            textObj.transform.SetParent(parent, false);
            textObj.transform.localPosition = localPos;

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 35;
            tm.color = Color.red;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;

            // 카메라를 바라보도록
            if (cam != null)
                textObj.transform.rotation = Quaternion.LookRotation(textObj.transform.position - cam.transform.position);

            debugObjects.Add(textObj);
        }

        // 선택된 Collider 빨간색 강조
        private static void HighlightSelectedCollider(Collider selected)
        {
            foreach (var kvp in debugRenderers)
            {
                foreach (var rend in kvp.Value)
                {
                    Color c = rend.material.color;
                    // 선택된 것만 빨강, 나머지는 원래 녹색 계열 유지
                    if (kvp.Key == selected)
                    {
                        c.r = 1f; c.g = 0f; c.b = 0f; // 빨강
                    }
                    else
                    {
                        // 원래 색 복원 (녹색 계열)
                        if (kvp.Key is CapsuleCollider)
                            c = new Color(0f, 1f, 0.5f, 0.5f);
                        else if (kvp.Key is SphereCollider)
                            c = new Color(0f, 1f, 0f, 0.5f);
                        else if (kvp.Key is BoxCollider)
                            c = new Color(0.5f, 1f, 0f, 0.5f);
                    }
                    rend.material.color = c;
                }
            }
        }

        //private static IEnumerator ExecuteAfterFrame(ObjectCtrlInfo objectCtrlInfo)
        //{
        //    int frameCount = 5;
        //    for (int i = 0; i < frameCount; i++)
        //        yield return null;
            
        //    ProcessDynamicBones(objectCtrlInfo);
        //}

        private static OCIFolder CreateOCIFolder(OCIChar ociChar, string folderName)
        {
            OIFolderInfo folderInfo = new OIFolderInfo(Studio.Studio.GetNewIndex());
            folderInfo.name = folderName;

            OCIFolder ociFolder = Studio.AddObjectFolder.Load(folderInfo, null, null, true, Studio.Studio.optionSystem.initialPosition);

            return ociFolder;            
        }

        private static OCICollider CreateOCICollider(OCIChar ociChar, Collider collider, Transform bone)
        {
            ChangeAmount changeAmount = new ChangeAmount(
                bone.localPosition,
                bone.localEulerAngles,
                bone.localScale
            );

            int idx = Studio.Studio.GetNewIndex();
            ColliderObjectInfo objectInfo = new ColliderObjectInfo(idx);            
            objectInfo.changeAmount = changeAmount;            
            
            OCICollider ociCollider = new OCICollider();
            ociCollider.objectInfo = objectInfo;
            ociCollider.collider = collider;

            GuideObject guideObject = Singleton<GuideObjectManager>.Instance.Add(collider.transform, idx);

            guideObject.mode = GuideObject.Mode.Local;
            guideObject.enablePos = true;
            guideObject.enableScale = true;
            guideObject.enableMaluti = false;
            guideObject.calcScale = false;
            guideObject.scaleRate = 0.025f;
            guideObject.scaleRot = 0.025f;
            guideObject.scaleSelect = 0.025f;
            guideObject.parentGuide = ociChar.guideObject;
            guideObject.SetVisibleCenter(true);
            guideObject.isActive = false;

            GuideObject guideObject2 = guideObject;
            guideObject2.isActiveFunc = (GuideObject.IsActiveFunc)Delegate.Combine(guideObject2.isActiveFunc, new GuideObject.IsActiveFunc(ociCollider.OnSelect));

            guideObject.parent = null;
            guideObject.nonconnect = false;
            guideObject.changeAmount = changeAmount;

            UnityEngine.Debug.Log($">> OCICollider {guideObject.objectSelect}, {guideObject.roots}, {guideObject.m_Enables}, {guideObject.gameObject} , {guideObject.isActiveFunc}");

            ociCollider.guideObject = guideObject;

            return ociCollider;
        }


        private static void ProcessDynamicBones(ObjectCtrlInfo objectCtrlInfo)
        {
            if (objectCtrlInfo != null)
            {
                UnityEngine.Debug.Log($">> ProcessDynamicBones ");

                OCIChar ociChar = objectCtrlInfo as OCIChar;

                List<GameObject> debugObjects = new List<GameObject>();

                _self._selectedOCI = objectCtrlInfo;
                _self._selectedOciSmr = GetBodyRenderer(_self._selectedOCI.guideObject.transformTarget);
                _self._pelvisBone = GetPelvisBone(_self._selectedOciSmr);

                // cloth 기존 삭제/ 새로 등록
                _self._originalMaxDistances.Clear();
                _self._clothes.Clear();

                Cloth[] cloths = _self._selectedOCI.guideObject.transformTarget.GetComponentsInChildren<Cloth>(true);
                foreach (var cloth in cloths)
                {
                    if (cloth == null || cloth.transform == null)
                        continue;

                    _self._clothes.Add(cloth);

                    cloth.stretchingStiffness = 0.5f; // 기본값보다 낮춤
                    cloth.damping = 1.0f; // 마찰 줄여서 더 잘 내려가게                    

                    // Max Distance 처리
                    ClothSkinningCoefficient[] coeffs = cloth.coefficients;
                    float[] maxDistances = new float[coeffs.Length];

                    for (int i = 0; i < coeffs.Length; i++)
                        maxDistances[i] = coeffs[i].maxDistance;

                    _self._originalMaxDistances.Add(cloth, maxDistances);
                }

                // cloth collider 기존 삭제/ 새로 등록
                foreach (KeyValuePair<TreeNodeObject, OCICollider> kvp in _self.clothesColliders)
                {
                    Singleton<Studio.Studio>.Instance.treeNodeCtrl.RemoveNode(kvp.Key);
                    Singleton<Studio.Studio>.Instance.dicInfo.Remove(kvp.Key);
                }


                if (_self._collider_folder_component == null)
                {
                   OCIFolder ociFolder = CreateOCIFolder(ociChar, "group: colliders");

                   GameObject folderGameObject = UnityEngine.Object.Instantiate<GameObject>(Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_ObjectNode);
                   folderGameObject.SetActive(true);
                   _self._collider_folder_component = folderGameObject.GetComponent<TreeNodeObject>();
                   _self._collider_folder_component.textName = "group: colliders";

                   Singleton<Studio.Studio>.Instance.treeNodeCtrl.AddNode(_self._collider_folder_component);
                   Singleton<Studio.Studio>.Instance.dicInfo.Add(_self._collider_folder_component, ociFolder);          
                }

                _self.clothesColliders.Clear();
                foreach (var bone in _self._selectedOciSmr.bones)
                {
                    SphereCollider[] colliders = bone.GetComponentsInChildren<SphereCollider>();
                    foreach (var col in colliders)
                    {
                        if (col == null) continue;

                        OCICollider ociCollider = CreateOCICollider(ociChar, col, bone);
                        GameObject gameObject = UnityEngine.Object.Instantiate<GameObject>(Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_ObjectNode);
                        gameObject.SetActive(true);
                        gameObject.transform.SetParent(Singleton<Studio.Studio>.Instance.treeNodeCtrl.m_ObjectRoot.transform, false);
                        TreeNodeObject collider_component = gameObject.GetComponent<TreeNodeObject>();
                        collider_component.textName = bone.name;

                        Singleton<Studio.Studio>.Instance.treeNodeCtrl.AddNode(collider_component);
                        Singleton<Studio.Studio>.Instance.dicInfo.Add(collider_component, ociCollider);
                        //Singleton<Studio.Studio>.Instance.treeNodeCtrl.SetParent(collider_component, _self._collider_folder_component);
                        _self.clothesColliders.Add(collider_component, ociCollider);
                        CreateSphereDebugWithName(col, bone.name, debugObjects);
                    }
                }
            }
        }                  

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {

            private static bool Prefix(object __instance, TreeNodeObject _node)
            {               
                UnityEngine.Debug.Log($">> nSelectSingle ");
                OCIChar ociChar = Studio.Studio.GetCtrlInfo(_node) as OCIChar;

                if (ociChar != null)
                {
                    ProcessDynamicBones(ociChar);

                    // OCICollider ociCollider = null;
                    // if (_self.clothesColliders.TryGetValue(_node, out ociCollider))
                    // {
                    //     HighlightSelectedCollider(ociCollider.collider);
                    // }
                }

                return true;
            }
        }

        // [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectMultiple))]
        // private static class WorkspaceCtrl_OnSelectMultiple_Patches
        // {
        //     private static bool Prefix(object __instance)
        //     {
        //         OCIChar ociChar = Studio.Studio.GetCtrlInfo(Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes[0]) as OCIChar;

        //         if (ociChar != null)
        //         {
        //             ProcessDynamicBones(ociChar);

        //             OCICollider ociCollider = null;
        //             if (_self.clothesColliders.TryGetValue(Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes[0], out ociCollider))
        //             {
        //                 HighlightSelectedCollider(ociCollider.collider);
        //             }                                         
        //         }
        //         return true;
        //     }
        // }

        // [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        // internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        // {
        //     private static bool Prefix(object __instance, TreeNodeObject _node)
        //     {
        //         if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
        //         {
        //             _self._status = Status.DESTORY;
        //             _self._selectedOCI = null;
        //         }

        //         return true;
        //     }
        // }

        [HarmonyPatch(typeof(OCIChar), "ChangeChara", new[] { typeof(string) })]
        internal static class OCIChar_ChangeChara_Patches
        {
            public static void Postfix(OCIChar __instance, string _path)
            {
                // ProcessDynamicBones(__instance as ObjectCtrlInfo);
            }
        }

        //[HarmonyPatch(typeof(ChaControl), "UpdateClothesStateAll")]
        //internal static class ChaControl_UpdateClothesStateAll_Patches
        //{
        //    public static void Postfix(ChaControl __instance)
        //    {
        //        if (__instance != null)
        //        {
        //            __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar() as OCIChar));
        //        }
        //    }
        //}

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                _self._status = Status.DESTORY;
                _self._selectedOCI = null;

                return true;
            }
        }

        #endregion
    }


    static class DebugMatUtil
    {
        public static void SetTransparent(Renderer rend, Color color, int queue = 3000)
        {
            if (rend == null) return;

            // Unlit/Color Shader 기반 새 머티리얼 생성
            Material mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = color;
            mat.renderQueue = queue;

            // 그림자 제거 (디버그용)
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            rend.material = mat;
        }
    }


    public class ColliderObjectInfo : ObjectInfo
    {
        public override int kind => 1;
        public ColliderObjectInfo(int _key) : base(_key)
        {
        }

        // kinds는 ObjectInfo에서 virtual이라 override 가능
        public override int[] kinds
        {
            get
            {
                // 그냥 자기 kind 하나만 리턴 (dummy)
                return new int[] { kind };
            }
        }

        public override void Save(BinaryWriter _writer, Version _version)
        {
            base.Save(_writer, _version);
            // ColliderObjectInfo 전용 저장 데이터가 있다면 여기에 추가
            // 지금은 dummy라서 없음
        }

        // Load - 기본 부모 동작 호출 + dummy 처리
        public override void Load(BinaryReader _reader, Version _version, bool _import, bool _other = true)
        {
            base.Load(_reader, _version, _import, _other);
            // ColliderObjectInfo 전용 로드 데이터가 있다면 여기에 추가
            // 지금은 dummy라서 없음
        }

        // DeleteKey는 abstract라 반드시 구현해야 함
        public override void DeleteKey()
        {
            // ColliderObjectInfo 전용 키 삭제 로직이 있다면 여기에
            // 지금은 dummy 처리
        }
    }

    public class OCICollider : ObjectCtrlInfo
    {
        public Collider collider;
        public override void OnDelete() { }

        public override void OnAttach(TreeNodeObject _parent, ObjectCtrlInfo _child)
        {
             UnityEngine.Debug.Log($">> OnAttach {_parent}, {_child}");
        }

        public override void OnDetach()
        {
            UnityEngine.Debug.Log($">> OnDetach ");
        }

        public override void OnDetachChild(ObjectCtrlInfo _child) {}

        public override void OnSelect(bool _select)
        {
            UnityEngine.Debug.Log($">> OnSelect {_select}");
        }

        public override void OnLoadAttach(TreeNodeObject _parent, ObjectCtrlInfo _child)
        {
            UnityEngine.Debug.Log($">> OnLoadAttach {_parent}");
        }

        public override void OnVisible(bool _visible)
        {
            UnityEngine.Debug.Log($">> OnVisible {_visible}");
        }        
    }
}
