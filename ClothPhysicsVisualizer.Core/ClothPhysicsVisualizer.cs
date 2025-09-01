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
using static Studio.GuideInput;
using static RootMotion.FinalIK.IKSolver;
using IllusionUtility.GetUtility;
using ADV.Commands.Object;
using static Illusion.Utils;

#endif

namespace ClothPhysicsVisualizer
{
#if BEPINEX
    [BepInPlugin(GUID, Name, Version)]
#if AISHOUJO || HONEYSELECT2
    [BepInProcess("StudioNEOV2")]
#endif
    [BepInDependency("com.bepis.bepinex.extendedsave")]
#endif
    public class ClothPhysicsVisualizer : GenericPlugin
#if IPA
                            , IEnhancedPlugin
#endif
    {
        #region Constants
        public const string Name = "ClothPhysicsVisualizerVisualizer";
        public const string Version = "0.9.0";
        public const string GUID = "com.alton.illusionplugins.ClothPhysicsVisualizer";
        internal const string _ownerId = "alton";
#if KOIKATSU || AISHOUJO || HONEYSELECT2
        private const int _saveVersion = 0;
        private const string _extSaveKey = "cloth_physics_visualizer";
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
        internal static ClothPhysicsVisualizer _self;

        internal static ConfigEntry<bool> ClothColliderVisual { get; private set; }

        private static string _assemblyLocation;
        private bool _loaded = false;

        private ObjectCtrlInfo _selectedOCI;

        private Dictionary<OCIChar, PhysicCollider> _ociCharColliders = new Dictionary<OCIChar, PhysicCollider>();

        internal enum Cloth_Status
        {
            CHAR_CHANGE,
            IDLE
        }

        internal enum Update_Mode
        {
            CHAR_CHANGE,
            DRESS_CHANGE
        }        

        #endregion

        #region Accessors
        #endregion


        #region Unity Methods
        protected override void Awake()
        {

            base.Awake();

            ClothColliderVisual = Config.Bind("Option", "Active", false, new ConfigDescription("Enable/Disable"));

            _self = this;
            Logger = base.Logger;

            _assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            ExtensibleSaveFormat.ExtendedSave.SceneBeingLoaded += OnSceneLoad;

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
        }


        private void OnSceneLoad(string path)
        {
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
        #endregion

        #region Patches

        private static Transform GetPelvisBone(SkinnedMeshRenderer smr)
        {
            return smr.rootBone;
        }

        // CapsuleCollider Wireframe 디버그
        private static void CreateCapsuleWireframe(CapsuleCollider capsule, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            Camera cam = Camera.main;

            // Root
            GameObject root = new GameObject(capsule.name + "_CapsuleWire");
            root.transform.SetParent(capsule.transform, false);
            root.transform.localPosition = capsule.center;
            root.transform.localRotation = Quaternion.identity;

            List<Renderer> renderers = new List<Renderer>();

            // Capsule 방향 결정
            Vector3 axis;
            Quaternion rot = Quaternion.identity;
            switch (capsule.direction)
            {
                case 0: axis = Vector3.right; rot = Quaternion.Euler(0f, 0f, 90f); break;   // X
                case 1: axis = Vector3.up; rot = Quaternion.identity; break;                 // Y
                case 2: axis = Vector3.forward; rot = Quaternion.Euler(90f, 0f, 0f); break; // Z
                default: axis = Vector3.up; break;
            }

            int segments = 48; // 원을 근사할 분할 수

            // Cylinder Body + Top/Bottom Hemisphere 선
            GameObject lineObj = new GameObject("CapsuleWireLines");
            lineObj.transform.SetParent(root.transform, false);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = Color.green;
            lr.widthMultiplier = 0.02f;

            List<Vector3> points = new List<Vector3>();

            float radius = capsule.radius;
            float halfHeight = capsule.height * 0.5f - radius;

            float angle_temp = 2 * Mathf.PI / segments;

            // Top Circle
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                points.Add(new Vector3(x, halfHeight, z));
            }

            // Bottom Circle
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                points.Add(new Vector3(x, -halfHeight, z));
            }

            // Cylinder side lines
            for (int i = 0; i <= segments; i++)
            {
                points.Add(new Vector3(Mathf.Cos(angle_temp * i) * radius, halfHeight, Mathf.Sin(angle_temp * i) * radius));
                points.Add(new Vector3(Mathf.Cos(angle_temp * i) * radius, -halfHeight, Mathf.Sin(angle_temp * i) * radius));
            }

            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());

            renderers.Add(lr); // LineRenderer 자체를 등록
            debugObjects.Add(lineObj);

            debugCollideRenderers[capsule] = renderers;

            // 텍스트
            Vector3 textPos = axis * (halfHeight * 0.5f + 0.1f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects, debugCollideRenderers);
        }

        // SphereCollider Wireframe 디버그
        private static void CreateSphereWireframe(SphereCollider sphere, string name, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            Camera cam = Camera.main;

            GameObject root = new GameObject(sphere.name + "_SphereWire");
            root.transform.SetParent(sphere.transform, false);
            root.transform.localPosition = sphere.center;
            root.transform.localRotation = Quaternion.identity;

            List<Renderer> renderers = new List<Renderer>();

            GameObject lineObj = new GameObject("SphereWireLines");
            lineObj.transform.SetParent(root.transform, false);
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.material = new Material(Shader.Find("Unlit/Color"));
            lr.material.color = Color.green;
            lr.widthMultiplier = 0.02f;

            List<Vector3> points = new List<Vector3>();
            int segments = 64; // 원 근사 분할 수
            float radius = sphere.radius;

            float angle_temp = 2 * Mathf.PI / segments;

            // XY 원
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
            // XZ 원
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            // YZ 원
            for (int i = 0; i <= segments; i++)
            {
                float angle = angle_temp * i;
                points.Add(new Vector3(0f, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }

            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());

            renderers.Add(lr);
            debugObjects.Add(lineObj);
            debugCollideRenderers[sphere] = renderers;

            // 텍스트
            Vector3 textPos = sphere.center + Vector3.up * (radius + 0.05f);
            CreateTextDebugLocal(root.transform, textPos, name, cam, debugObjects, debugCollideRenderers);
        }

        // Text 도 bone 기준으로 움직이도록 수정
        private static void CreateTextDebugLocal(Transform parent, Vector3 localPos, string text, Camera cam, List<GameObject> debugObjects, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            GameObject textObj = new GameObject(text + "_" + "TextDebug");
            textObj.transform.SetParent(parent, false);
            textObj.transform.localPosition = localPos;

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 35;
            tm.color = Color.yellow;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;

            // 카메라를 바라보도록
            if (cam != null)
                textObj.transform.rotation = Quaternion.LookRotation(textObj.transform.position - cam.transform.position);

            debugObjects.Add(textObj);
        }

        // 선택된 Collider 빨간색 강조
        private static void HighlightSelectedCollider(Collider selected, Dictionary<Collider, List<Renderer>> debugCollideRenderers)
        {
            foreach (var kvp in debugCollideRenderers)
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

        private static OCIFolder CreateOCIFolder(OCIChar ociChar, string folderName, TreeNodeObject parentNode)
        {
            OIFolderInfo folderInfo = new OIFolderInfo(Studio.Studio.GetNewIndex());
            folderInfo.name = folderName;

            OCIFolder ociFolder = Studio.AddObjectFolder.Load(folderInfo, null, null, true, Studio.Studio.optionSystem.initialPosition);

            List<TreeNodeObject> newChild = new List<TreeNodeObject>();

            foreach (var child in parentNode.m_child)
            {
                if (child != null)
                {
                    newChild.Add(child);
                }
            }
            parentNode.m_child = newChild;

            ociFolder.treeNodeObject.SetParent(parentNode);

            ociFolder.treeNodeObject.enableDelete = false;
            ociFolder.treeNodeObject.enableCopy = false;
            ociFolder.treeNodeObject.enableChangeParent = false;

            return ociFolder;
        }

        private static OCICollider CreateOCICollider(OCIChar ociChar, Collider collider, Transform bone, string name, TreeNodeObject parentNode)
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
            ociCollider.ociChar = ociChar;
            ociCollider.objectInfo = objectInfo;
            ociCollider.collider = collider;

            GuideObject guideObject = Singleton<GuideObjectManager>.Instance.Add(collider.transform, idx);

            if (guideObject != null)
            {
                guideObject.enablePos = true;
                guideObject.enableScale = true;
                guideObject.enableMaluti = true;
                guideObject.calcScale = true;
                guideObject.scaleRate = 0.025f;
                guideObject.scaleRot = 0.025f;
                guideObject.scaleSelect = 0.025f;
                guideObject.SetVisibleCenter(true);
                guideObject.isActive = false;

                GuideObject guideObject2 = guideObject;
                guideObject2.isActiveFunc = (GuideObject.IsActiveFunc)Delegate.Combine(guideObject2.isActiveFunc, new GuideObject.IsActiveFunc(ociCollider.OnSelect));

                guideObject.parent = null;
                guideObject.nonconnect = false;
                guideObject.changeAmount = changeAmount;

                ociCollider.guideObject = guideObject;
                ociCollider.treeNodeObject = Studio.Studio.AddNode(name);
                ociCollider.treeNodeObject.SetParent(parentNode);
                ociCollider.treeNodeObject.enableChangeParent = false;
                ociCollider.treeNodeObject.enableAddChild = false;
                ociCollider.treeNodeObject.enableDelete = false;
                ociCollider.treeNodeObject.enableCopy = false;

                Studio.Studio.AddCtrlInfo(ociCollider);
             
                return ociCollider;
            }

            return null;
        }

        private static void InitCollider(PhysicCollider value)
        {
            foreach (var obj in value.debugCapsuleCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }
            foreach (var obj in value.debugSphereCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }
            if (value.flatGroundClothColliderObj != null) { Destroy(value.flatGroundClothColliderObj); }

            value.debugCapsuleCollideVisibleObjects.Clear();
            value.debugSphereCollideVisibleObjects.Clear();           
            value.debugCollideRenderers.Clear();                 
        }

        private static void ClearPhysicCollier(PhysicCollider value)
        {
            UnityEngine.Debug.Log($">> ClearPhysicCollier");
            TreeNodeObject selectedTreeNodeObj = null;

            if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() > 0)
            {
                selectedTreeNodeObj = Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes[0];
            }

            foreach (var obj in value.debugCapsuleCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }
            foreach (var obj in value.debugSphereCollideVisibleObjects)
            {
                if (obj == null) continue;
                GameObject.Destroy(obj);
            }

            value.debugCapsuleCollideVisibleObjects.Clear();
            value.debugSphereCollideVisibleObjects.Clear();

            if (value.flatGroundClothColliderObj != null) { Destroy(value.flatGroundClothColliderObj); }


            if (value.ociCFolder != null)
            {
                value.ociCFolder.treeNodeObject.enableDelete = true;
                value.ociSFolder.treeNodeObject.enableDelete = true;

                foreach (var obj in value.ociCFolderChild.Keys)
                {
                    if (obj == null) continue;

                    obj.enableDelete = true;
                }

                foreach (var obj in value.ociSFolderChild.Keys)
                {
                    if (obj == null) continue;

                    obj.enableDelete = true;
                }
                Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNodeLoop(value.ociSFolder.treeNodeObject);
                Singleton<Studio.Studio>.Instance.treeNodeCtrl.DeleteNodeLoop(value.ociCFolder.treeNodeObject);
            }

            value.ociCFolderChild.Clear();
            value.ociSFolderChild.Clear();
            value.debugCollideRenderers.Clear();
            _self._ociCharColliders.Remove(value.ociChar);

            if (value.ociCFolder != null && selectedTreeNodeObj != null)
            {
                Singleton<Studio.Studio>.Instance.treeNodeCtrl.SelectSingle(selectedTreeNodeObj);
            }
        }

        private static List<CapsuleCollider> GetChildCapsuleColliders(Transform bone)
        {
            var result = new List<CapsuleCollider>();
            if (bone == null) return result;

            foreach (Transform child in bone)
            {
                var colliders = child.GetComponents<CapsuleCollider>();
                if (colliders != null && colliders.Length > 0)
                {
                    result.AddRange(colliders);
                }
            }

            return result;
        }

        private static CapsuleCollider AddCapsuleFlatCollider(GameObject colliderObject, Transform bone)
        {
            colliderObject.transform.SetParent(bone, false);

            var capsule = colliderObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0, -5.5f, 0);
            capsule.radius = 6f;
            capsule.height = 1.0f;
            capsule.direction = 1; // Y축 기준

            return capsule;
        }

        private static void CreateGroundClothCollider(PhysicCollider physicCollider, ChaControl baseCharConctrol, List<Cloth> clothes)
        {
            // UnityEngine.Debug.Log($">> CreateGroundClothCollider");
            // ground collider
            Transform root_bone = baseCharConctrol.objBodyBone.transform.FindLoop("cf_J_Root");
            // SphereCollider roundCollider = null;
            CapsuleCollider flatCollider = null;
            {
                physicCollider.flatGroundClothColliderObj = new GameObject("Cloth colliders support_flat_ground");
                flatCollider = AddCapsuleFlatCollider(physicCollider.flatGroundClothColliderObj, root_bone);

                foreach (Cloth cloth in clothes)
                {
                    var existingCapsules = cloth.capsuleColliders ?? new CapsuleCollider[0];

                    // 새 capsuleCollider 추가
                    cloth.capsuleColliders = existingCapsules
                        .Concat(new CapsuleCollider[] { flatCollider })
                        .ToArray();
                }
            }
        }

        private static void AddVisualColliders(OCIChar ociChar, Update_Mode type)
        {
            if (ociChar != null && ClothColliderVisual.Value == true)
            {
                UnityEngine.Debug.Log($">> AddVisualColliders");

                PhysicCollider physicCollider = null;

                if (type == Update_Mode.CHAR_CHANGE) 
                {
                    if (_self._ociCharColliders.TryGetValue(ociChar, out physicCollider))
                    {
                        UnityEngine.Debug.Log($">> reused cache");
                        return;
                    }
                }

                UnityEngine.Debug.Log($">> register cache");

                if (_self._ociCharColliders.TryGetValue(ociChar, out physicCollider))
                {
                    ClearPhysicCollier(physicCollider);
                }

                physicCollider = new PhysicCollider();
                physicCollider.ociChar = ociChar;
                _self._selectedOCI = ociChar;

                ChaControl baseCharConctrol = ociChar.charInfo;

                List<GameObject> physicsClothes = new List<GameObject>();

                int idx = 0;
                foreach (var cloth in baseCharConctrol.objClothes)
                {                    
                    if (cloth == null)
                    {
                        idx++;
                        continue;
                    }
                    
                    physicCollider.clothInfos[idx].clothObj = cloth;

                    if (cloth.GetComponentsInChildren<Cloth>().Length > 0)
                    {
                        physicCollider.clothInfos[idx].hasCloth = true;
                        physicsClothes.Add(cloth);
                    }
                    else
                    {
                        physicCollider.clothInfos[idx].hasCloth = false;
                    }

                    idx++;
                }

                if (physicsClothes.Count > 0)
                {
                    ociChar.treeNodeObject.enableAddChild = true;
                    physicCollider.ociCFolder = CreateOCIFolder(ociChar, "Group: (C)Colliders", ociChar.treeNodeObject);
                    physicCollider.ociSFolder = CreateOCIFolder(ociChar, "Group: (S)Colliders", ociChar.treeNodeObject);

                    physicCollider.ociCFolder.treeNodeObject.enableAddChild = true;
                    physicCollider.ociSFolder.treeNodeObject.enableAddChild = true;


                    List<Cloth> clothes = new List<Cloth>();

                    foreach (var cloth in physicsClothes)
                    {
                        clothes.Add(cloth.GetComponentsInChildren<Cloth>()[0]);
                    }

                    CreateGroundClothCollider(physicCollider, baseCharConctrol, clothes);
                }

                {
                    // sphere collider
                    if (baseCharConctrol.objBodyBone != null && physicCollider.ociSFolder != null)
                    {

                        List<SphereCollider> scolliders = baseCharConctrol.objBodyBone
                            .transform
                            .GetComponentsInChildren<SphereCollider>()
                            .OrderBy(col => col.gameObject.name) // 이름 기준 정렬
                            .ToList();


                        foreach (var col in scolliders)
                        {
                            if (col == null) continue; // Destroy 된 경우 스킵

                            if (col.gameObject.name.Contains("Cloth colliders"))
                            {
                                string trim_name = col.gameObject.name.Replace("Cloth colliders support_", "").Trim();
                                string bone_name;

                                idx = trim_name.IndexOf('-');
                                if (idx >= 0)
                                    bone_name = trim_name.Substring(0, idx);
                                else
                                    bone_name = trim_name;

                                OCICollider ociCollider = CreateOCICollider(ociChar, col, col.gameObject.transform, bone_name, physicCollider.ociSFolder.treeNodeObject);

                                if (ociCollider != null)
                                {
                                    physicCollider.ociSFolderChild.Add(ociCollider.treeNodeObject, col);
                                    CreateSphereWireframe(col, bone_name, physicCollider.debugSphereCollideVisibleObjects, physicCollider.debugCollideRenderers);
                                }
                            }
                        }

                        // capsule collider
                        List<CapsuleCollider> ccolliders = baseCharConctrol.objBodyBone
                            .transform
                            .GetComponentsInChildren<CapsuleCollider>()
                            .OrderBy(col => col.gameObject.name) // 이름 기준 정렬
                            .ToList();

                        foreach (var col in ccolliders)
                        {

                            if (col == null) continue; // Destroy 된 경우 스킵

                            if (col.gameObject.name.Contains("Cloth colliders"))
                            {
                                string trim_name = col.gameObject.name.Replace("Cloth colliders support_", "").Trim();
                                string bone_name;
                                idx = trim_name.IndexOf('-');
                                if (idx >= 0)
                                    bone_name = trim_name.Substring(0, idx);
                                else
                                    bone_name = trim_name;

                                OCICollider ociCollider = CreateOCICollider(ociChar, col, col.gameObject.transform, bone_name, physicCollider.ociCFolder.treeNodeObject);

                                if (ociCollider != null)
                                {
                                    physicCollider.ociCFolderChild.Add(ociCollider.treeNodeObject, col);
                                    CreateCapsuleWireframe(col, bone_name, physicCollider.debugCapsuleCollideVisibleObjects, physicCollider.debugCollideRenderers);
                                }
                            }
                        }
                    }

                    if (physicCollider.ociCFolder != null)
                    {
                        ociChar.treeNodeObject.enableAddChild = false;
                        physicCollider.ociCFolder.treeNodeObject.enableAddChild = false;
                        physicCollider.ociSFolder.treeNodeObject.enableAddChild = false;
                    }

                    UnityEngine.Debug.Log($">> physicCollider {physicCollider.clothInfos[0].clothObj}");
                    _self._ociCharColliders.Add(ociChar, physicCollider);

                    // parent 구성 후 UI 업데이트                 
                    Singleton<Studio.Studio>.Instance.treeNodeCtrl.RefreshHierachy();
                }
            }
        }

        private static IEnumerator ExecuteAfterFrame(OCIChar ociChar, Update_Mode type)
        {
            int frameCount = 30;
            for (int i = 0; i < frameCount; i++)
                yield return null;

            AddVisualColliders(ociChar, type);
        }

        private static void DeselectNode(OCIChar ociChar)
        {
            if (ociChar != null)
            {
                PhysicCollider value = null;
                if (_self._ociCharColliders.TryGetValue(ociChar, out value))
                {
                    ClearPhysicCollier(value);
                    _self._ociCharColliders.Remove(ociChar);
                }

                _self._selectedOCI = null;

            }
        }

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnSelectSingle), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnSelectSingle_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {
                OCIChar ociChar = Studio.Studio.GetCtrlInfo(_node) as OCIChar;

                if (ociChar != null)
                {
                    ociChar.GetChaControl().StartCoroutine(ExecuteAfterFrame(ociChar, Update_Mode.CHAR_CHANGE));
                }

                OCICollider ociCollider = Studio.Studio.GetCtrlInfo(_node) as OCICollider;
                if (ociCollider != null)
                {

                    if (_node.parent != null)
                    {
                        if (_self._ociCharColliders.TryGetValue(ociCollider.ociChar, out var physicCollider))
                        {
                            Collider collider = null;
                            if (physicCollider.ociCFolderChild.TryGetValue(_node, out collider))
                            {
                                HighlightSelectedCollider(collider, physicCollider.debugCollideRenderers);
                            }

                            if (physicCollider.ociSFolderChild.TryGetValue(_node, out collider))
                            {
                                HighlightSelectedCollider(collider, physicCollider.debugCollideRenderers);
                            }
                        }
                    }
                }

                return true;
            }
        }

        // [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeselectSingle), typeof(TreeNodeObject))]
        // internal static class WorkspaceCtrl_OnDeselectSingle_Patches
        // {
        //     private static bool Prefix(object __instance, TreeNodeObject _node)
        //     {
        //         if (Singleton<Studio.Studio>.Instance.treeNodeCtrl.selectNodes.Count() == 0)
        //         {
        //             if (_self._selectedOCI != null)
        //             {
        //                 DeselectNode(_self._selectedOCI);
        //                 _self._selectedOCI = null;
        //             }
        //         }

        //         return true;
        //     }
        // }    

        [HarmonyPatch(typeof(WorkspaceCtrl), nameof(WorkspaceCtrl.OnDeleteNode), typeof(TreeNodeObject))]
        internal static class WorkspaceCtrl_OnDeleteNode_Patches
        {
            private static bool Prefix(object __instance, TreeNodeObject _node)
            {

                OCIChar ociChar = Studio.Studio.GetCtrlInfo(_node) as OCIChar;
                DeselectNode(ociChar);

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
                    chaControl.StartCoroutine(ExecuteAfterFrame(__instance as OCIChar, Update_Mode.CHAR_CHANGE));
                }
            }
        }

        // 옷 부분 변경
        [HarmonyPatch(typeof(ChaControl), "ChangeClothes", typeof(int), typeof(int), typeof(bool))]
        private static class ChaControl_ChangeClothes_Patches
        {
            private static void Postfix(ChaControl __instance, int kind, int id, bool forceChange)
            {
                UnityEngine.Debug.Log($">> ChangeClothes");
                bool shouldReallocation = true;
                PhysicCollider physicCollider = null;
                if (_self._ociCharColliders.TryGetValue(__instance.GetOCIChar(), out physicCollider))
                {
                    if (physicCollider.clothInfos[kind] != null)
                    {                    
                        GameObject newClothObj = __instance.objClothes[kind];

                        if (newClothObj != null)
                        {
                            if (physicCollider.clothInfos[kind].hasCloth == false && newClothObj.GetComponentsInChildren<Cloth>().Length == 0)
                            {
                                shouldReallocation = false;
                            }
                        }
                                          
                        // UnityEngine.Debug.Log($">> oldClothObj, {physicCollider.clothInfos[kind].hasCloth}");
                        if (newClothObj != null)
                            UnityEngine.Debug.Log($">> newClothObj, {newClothObj.name}, {newClothObj.GetComponentsInChildren<Cloth>().Length}");

                    }

                    if (shouldReallocation)
                        __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar(), Update_Mode.DRESS_CHANGE));   
                }                    
            }
        }

        // 옷 전체 변경
        [HarmonyPatch(typeof(ChaControl), nameof(ChaControl.SetAccessoryStateAll), typeof(bool))]
        internal static class ChaControl_SetAccessoryStateAll_Patches
        {
            public static void Postfix(ChaControl __instance, bool show)
            {
                if (__instance != null)
                {
                    __instance.StartCoroutine(ExecuteAfterFrame(__instance.GetOCIChar(), Update_Mode.DRESS_CHANGE));  
                }
            }
        }

        [HarmonyPatch(typeof(Studio.Studio), "InitScene", typeof(bool))]
        private static class Studio_InitScene_Patches
        {
            private static bool Prefix(object __instance, bool _close)
            {
                foreach (var kvp in _self._ociCharColliders)
                {
                    var key = kvp.Key;
                    PhysicCollider value = kvp.Value;
                    InitCollider(value);
                }

                _self._ociCharColliders.Clear();
                _self._selectedOCI = null;

                return true;
            }
        }

        [HarmonyPatch(typeof(OCIFolder), "OnVisible", typeof(bool))]
        internal static class OCIFolder_OnVisible_Patches
        {
            public static void Postfix(OCIFolder __instance, bool _visible)
            {
                if (__instance.treeNodeObject == null || __instance.treeNodeObject.parent == null)
                    return;

                OCIChar ociChar = Studio.Studio.GetCtrlInfo(__instance.treeNodeObject.parent) as OCIChar;

                if (ociChar != null)
                {
                    if (_self._ociCharColliders.TryGetValue(ociChar, out var physicCollider))
                    {
                        if (__instance.folderInfo.name == "Group: (C)Colliders")
                        {
                            foreach (var visibleObject in physicCollider.debugCapsuleCollideVisibleObjects)
                            {
                                visibleObject.SetActive(_visible);
                            }
                        }
                        else if (__instance.folderInfo.name == "Group: (S)Colliders")
                        {
                            foreach (var visibleObject in physicCollider.debugSphereCollideVisibleObjects)
                            {
                                visibleObject.SetActive(_visible);
                            }
                        }
                    }
                }
            }
        }
        
        #endregion
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

    public class ClothInfo
    {
        public GameObject clothObj;
        public bool hasCloth;
    }

    public class PhysicCollider
    {
        public OCIChar ociChar;
        public ClothInfo[] clothInfos;

        public SkinnedMeshRenderer selectedOciSmr;

        public List<GameObject> debugCapsuleCollideVisibleObjects = new List<GameObject>();

        public List<GameObject> debugSphereCollideVisibleObjects = new List<GameObject>();

        public Dictionary<Collider, List<Renderer>> debugCollideRenderers = new Dictionary<Collider, List<Renderer>>();

        // public Dictionary<DynamicBoneCollider, List<Renderer>> debugDynamicRenderers = new Dictionary<DynamicBoneCollider, List<Renderer>>();

        public OCIFolder ociCFolder;
        public OCIFolder ociSFolder;

        public Dictionary<TreeNodeObject, Collider> ociCFolderChild = new Dictionary<TreeNodeObject, Collider>();

        public Dictionary<TreeNodeObject, Collider> ociSFolderChild = new Dictionary<TreeNodeObject, Collider>();

        public GameObject flatGroundClothColliderObj;

        public PhysicCollider()
        {
            clothInfos = new ClothInfo[8];
            for (int i = 0; i < clothInfos.Length; i++)
            {
                clothInfos[i] = new ClothInfo();
            }
        }        
    }

    public class OCICollider : ObjectCtrlInfo
    {
        public OCIChar ociChar;
        public Collider collider;
        public override void OnDelete() { }

        public override void OnAttach(TreeNodeObject _parent, ObjectCtrlInfo _child)
        {
            //  UnityEngine.Debug.Log($">> OnAttach {_parent}, {_child}");
        }

        public override void OnDetach()
        {
            // UnityEngine.Debug.Log($">> OnDetach ");
        }

        public override void OnDetachChild(ObjectCtrlInfo _child) { }

        public override void OnSelect(bool _select)
        {
            // UnityEngine.Debug.Log($">> OnSelect {_select}");
        }

        public override void OnLoadAttach(TreeNodeObject _parent, ObjectCtrlInfo _child)
        {
            // UnityEngine.Debug.Log($">> OnLoadAttach {_parent}");
        }

        public override void OnVisible(bool _visible)
        {
            // UnityEngine.Debug.Log($">> OnVisible {_visible}");
        }
    }
}