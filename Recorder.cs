using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using StoryGenerator.Utilities;
using StoryGenerator.SceneState;
using System;
using System.Text;
using CalcObjectPosition;
using UnityEngine.UI;
using Newtonsoft.Json;

namespace StoryGenerator.Recording
{

    public class ExecutionError
    {
        public string Message { get; internal set; }

        public ExecutionError(string message)
        {
            Message = message;
        }
    }

    public class Recorder : MonoBehaviour
    {
        public List <string> imageSynthesis = new List<string>();
        public bool savePoseData = false;
        public bool saveSceneStates = false;

        public string OutputDirectory { get; set; }
        public int MaxFrameNumber { get; set; }
        public string FileName { get; set; }
        public Animator Animator { get; internal set; }
        public ExecutionError Error { get; set; }
        public List<ICameraControl> CamCtrls { get; set; }

        public SceneStateSequence sceneStateSequence { get; set; } = new SceneStateSequence();

        // Index of the character assigned to this camera controller
        public int charIdx { get; set; }
        public List<string> currentCameraMode;

        List<ActionRange> actionRanges = new List<ActionRange>();
        List<CameraData> cameraData = new List<CameraData>();
        List<PoseData> poseData = new List<PoseData>();
        int frameRate = 20;
        int frameNum = 0;
        public int currentframeNum = 0;
        bool recording = false;
        // Used to skip optic flow frame generation upon camera transition
        // Initial value is true since the first frame has bad optical flow
        bool isCameraChanged = true;

        const int INITIAL_FRAME_SKIP = 2;   
        public int ImageWidth = 640; // 375;
        public int ImageHeight = 480; //250;
        public int _per_frame = 5;   // Add Oct/2022

        // Get target position in screen coordinate added 2022
        [Header("Object Rect in Screen Coordinate")]
        public bool _calcRectALL;// Calc Rect of all objects
        public bool _calcRect;// Calc Rect of Object 
        public bool _outGraph;// out put graph
        public bool _calcRectChar;// Calc R
        public bool _withRect; // switch out put with rect or not
        //public GameObject _targetGO;
        public GameObject _RectUIObject;
        public GameObject _RectUICharacter;
        public int _space;
        public Text _textObject;
        public Text _textCharacter;
        public Text _textAction;
        public Text _textTargetObject;
        //public Text _textChar;
        //private Rect _rect;
        private RectTransform _rectTransformObject;
        private RectTransform _rectTransformCharacter;
        //private Renderer _renderer;
        //private string _myStr;
        //private Rect _camViewPortRect;
        //private string _className;  // class name of graph node for visibility check
        //private int _id;            // id of graph node for visibility check
        // for json  serializ
        private VisibleObjectData _vod;
        //private VisibleObject _vo;
        //private bool _youcansee;
    
        // for update graph node...
        private bool _canObjectStateUpdate;
        private string _eoName;
        private int _eoId;
        private  Utilities.ObjectState _os;
        private bool _canGrabbedEdgeUpdata;
        //private GameObject _grrabbedGO;
        private String _HandGrabbed;
       
        // do not use globals :(
        //private List<GameObject> _tagetGOChar = new List<GameObject>();
        private struct VisibleRect
        {
            public Rect rect;
            public bool vis;
            public Color color;
        }

        private struct VisTargetObject
        {
            public string className;
            public int id;
            public GameObject target;
            //public Renderer[] renderer;
        }
        
        VisTargetObject _visTargetObject = new VisTargetObject();

        private struct VisRoomObject
        {
            public string className;
            public int id;

            public GameObject room;
        }

        VisRoomObject _visRoomObject = new VisRoomObject();

        // ======================================================================================== //
        // ==================================                    ================================== //
        // ======================================================================================== //

        private EnvironmentGraphCreator _currentGraphCreator = null;
        private EnvironmentGraph _currentGraph;
        //private HashSet<Tuple<int, int, ObjectRelation>> _edgeSet;
        private List<CharacterControl> _characters;
        //private EnvironmentObject _targetEO;
        private Transform _transform;

        //private string _currentCollision;

        // ======================================================================================== //
        // ================================== Class Declarations ================================== //
        // ======================================================================================== //

        private class ActionRange
        {
            string action; // For sub actions, action is printed
            int scriptLine;
            int frameStart;
            int frameEnd;

            public string Action{get{return action;}}
            public int ScriptLine{get{return scriptLine;}}
            public int FrameStart{get{ return frameStart;}}
            public int FrameEnd{get{ return frameEnd;}}

            public ActionRange(int scriptLine, string actionStr, int frameNum)
            {
                this.scriptLine = scriptLine;
                action = actionStr;
                frameStart = frameNum;
            }

            public void MarkActionEnd(int frameNum)
            {
                frameEnd = frameNum;
            }

            public string GetString()
            {
                return string.Format("{0} {1} {2} {3}", scriptLine, action, frameStart, frameEnd);
            }

        }

        private class CameraData
        {
            public Matrix4x4 ProjectionMatrix { get; set; }
            public Matrix4x4 World2CamMatrix { get; set; }
            public int FrameStart { get; set; }
            public int FrameEnd { get; set; }

            override public string ToString()
            {
                return string.Format("{0}{1} {2} {3}",
                    ProjectionMatrix.ToString().Replace('\t', ' ').Replace('\n', ' '),
                    World2CamMatrix.ToString().Replace('\t', ' ').Replace('\n', ' '),
                    FrameStart, FrameEnd);
            }
        }

        private class PoseData
        {
            private static HumanBodyBones[] bones = (HumanBodyBones[])Enum.GetValues(typeof(HumanBodyBones));

            int frameNumber;
            private Vector3[] boneVectors = new Vector3[bones.Length];

            public PoseData(int frameNumber, Animator animator)
            {
                this.frameNumber = frameNumber;
                for (int i = 0; i < bones.Length; i++) {
                    if (bones[i] < 0 || bones[i] >= HumanBodyBones.LastBone)
                    {
                        continue;
                    }
                    Transform bt = animator.GetBoneTransform(bones[i]);

                    if (bt != null)
                        boneVectors[i] = bt.position;
                }
            }

            public static string BoneNamesToString()
            {
                return string.Join(" ", bones.Select(hbb => hbb.ToString()).ToArray());
            }

            override public string ToString()
            {
                StringBuilder sb = new StringBuilder();

                sb.Append(frameNumber);
                foreach (Vector3 v in boneVectors) {
                    sb.Append(' '); sb.Append(v.x);
                    sb.Append(' '); sb.Append(v.y);
                    sb.Append(' '); sb.Append(v.z);
                }
                return sb.ToString();
            }
        }

        //
        //  Add 2022 for recording coordinate values in screen  space
        //

        [Serializable]
        public class VisibleObjectData
        {
            public List<VisibleObject> voList = new List<VisibleObject>();
        }

        [Serializable]
        public struct VisibleObject
        {   
            public string name;
            public int id;
            public int frameId;
            public bool visible;
            public Vector2Int leftTop;
            public Vector2Int rightBottom;
            public string predicate;
        }

        [Serializable]
        public struct VisibleChar
        {
            public string name;
            public int id;
            public int frameId;
            public bool visible;
            public Vector2Int leftTop;
            public Vector2Int rightBottom;
        }


        // ======================================================================================== //
        // ====================================== Properties ====================================== //
        // ======================================================================================== //

        public bool Recording
        {
            get { return recording; }
            set {
                if (value == false) {
                    MarkActionEnd();
                    MarkCameraEnd();
                }
                recording = value;
            }
        }

        public int FrameRate
        {
            get { return frameRate; }
            set {
                // optical flow sentivity should be proportional to the framerate
                ImageSynthesis.OPTICAL_FLOW_SENSITIVITY = value;
                frameRate = value;
            }
        }

        // ======================================================================================== //
        // =============================== Monobehaviour Executions =============================== //
        // ======================================================================================== //

        public void Initialize()
        {
            Time.captureFramerate = frameRate;

            Time.captureFramerate = frameRate;

            for (int cam_id = 0; cam_id < CamCtrls.Count; cam_id++)
            {
                if (CamCtrls[cam_id] != null)
                    CamCtrls[cam_id].Update();
            }

            if (OutputDirectory != null)
            {
                const string FILE_NAME_PREFIX = "Action_";
                StartCoroutine(OnEndOfFrame(Path.Combine(OutputDirectory, FILE_NAME_PREFIX)));
            }
        }

        // Add Oct/2022

        void OnValidate()
        {
            _rectTransformObject = _RectUIObject.GetComponent<RectTransform>();
            _rectTransformCharacter = _RectUICharacter.GetComponent<RectTransform>();

            Vector3 rectPosition = new Vector3( ImageWidth * 0.5f, ImageHeight * 0.5f, 0f);
            _rectTransformObject.transform.position = rectPosition;
            _rectTransformCharacter.transform.position = rectPosition;
            Vector2 rectSize = new Vector2(ImageWidth, ImageHeight);
            _rectTransformObject.sizeDelta = rectSize;
            _rectTransformCharacter.sizeDelta = rectSize;
        }

        
        // yes init by Unity... Added 2022
        void Start()
        {
            //_rect = new Rect();
            //if(_calcRect == true){
            _rectTransformObject = _RectUIObject.GetComponent<RectTransform>();
            _rectTransformCharacter = _RectUICharacter.GetComponent<RectTransform>();

            _RectUIObject.GetComponent<Image>().color = _textObject.color = Color.red;
            _RectUICharacter.GetComponent<Image>().color = _textCharacter.color = Color.red;
            _textAction.color = _textTargetObject.color = Color.red;

            // Add Oct/2022
            // get command line args and set size of rects
            string[] args = System.Environment.GetCommandLineArgs();
            float rectWidth = ImageWidth;
            float rectHeight = ImageHeight;
            for (int i = 0; i < args.Length; i++)
            {

                switch(args[i])
                {
                    case "-screen-width":
                        rectWidth = float.Parse(args[i+1]);
                        break;
                    case "-screen-height":
                        rectHeight = float.Parse(args[i+1]);
                        break;
                    default:
                        break;
                }

            }

            Vector3 rectPosition = new Vector3( rectWidth * 0.5f, rectHeight * 0.5f, 0f);
            _rectTransformObject.transform.position = rectPosition;
            _rectTransformCharacter.transform.position = rectPosition;
            Vector2 rectSize = new Vector2(rectWidth, rectHeight);
            _rectTransformObject.sizeDelta = rectSize;
            _rectTransformCharacter.sizeDelta = rectSize;

            _textCharacter.text = "No Character";
            _textObject.text = "No Object   " + rectSize.ToString("0000");
            _textAction.text = "No Action";
            //_renderer = _targetGO.GetComponent<Renderer>();
            //_camViewPortRect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            //}
            
            _vod = new VisibleObjectData();
            //_vo = new VisibleObject();

            _canObjectStateUpdate = false;
            _canGrabbedEdgeUpdata = false;
        }

        // ======================================================================================== //
        // ================================== Methods - Actions =================================== //
        // ======================================================================================== //

        public void MarkActionStart(InteractionType a, int scriptLine)
        {
            // For current implementation, MarkActionEnd and MarkActionStart happens on the same frame
            // Include offset to prevent this.
            MarkActionEnd();
            actionRanges.Add(new ActionRange(scriptLine, a.ToString(), frameNum > 0 ? frameNum + 1 : 0));
            foreach (ActionRange ar in actionRanges)
            {
                Debug.Log("currentFrame " + frameNum + "  scriptLine " + ar.ScriptLine + "  action " + ar.Action + "  frameStart " + ar.FrameStart + "  frameEnd " + ar.FrameEnd);
            }
            
            // Add Oct/2022
            _textAction.color = Color.green;
            _textAction.text = a.ToString();
        }

        // This marks the end of execution so that intentional delay after executing all actions
        // doesn't corrupt ground truth data.
        public void MarkTermination()
        {
            // const string NULL_ACTION = "NULL";
            MarkActionEnd();
            // actionRanges.Add(new ActionRange(-1, NULL_ACTION, frameNum + 1));
        }

        private void MarkActionEnd()
        {
            // Always update the last element in the list
            if (actionRanges.Count > 0)
                actionRanges[actionRanges.Count - 1].MarkActionEnd(frameNum);
        }

        // ======================================================================================== //
        // ================================== Methods - Cameras =================================== //
        // ======================================================================================== //

        public void UpdateCameraData(Camera newCamera)
        {
            isCameraChanged = true;
            MarkCameraEnd();
            cameraData.Add(new CameraData() { ProjectionMatrix = newCamera.projectionMatrix, FrameStart = frameNum > 0 ? frameNum + 1 : 0 });
        }

        void MarkCameraEnd()
        {
            if (cameraData.Count > 0)
                cameraData[cameraData.Count - 1].FrameEnd = frameNum;
        }

        // ======================================================================================== //
        // ============================== Methods - Saving/Rendering ============================== //
        // ======================================================================================== //

        // Single point where we save/render/record things. WaitForEndOfFrame does not always align
        // Update() or LateUpdate(). One might get called few times before the other or vice versa.
        // This shows similar issue:
        // https://forum.unity.com/threads/yield-return-waitendofframe-will-wait-until-end-of-the-same-frame-or-the-next-frame.282213/
        System.Collections.IEnumerator OnEndOfFrame(string pathPrefix)
        {
            if (CamCtrls.Count > 0)
            {
                for (int i = 0; i < INITIAL_FRAME_SKIP; i++)
                {
                    yield return new WaitForEndOfFrame();
                    for (int cam_id = 0; cam_id < CamCtrls.Count; cam_id++)
                    {
                        CamCtrls[cam_id].Update();
                    }
                }

            }

            // Need to check since recording can be disabled due to error such as stuck error.
            while (recording && currentframeNum <= MaxFrameNumber) {
                yield return new WaitForEndOfFrame();

                if (recording) {
                    for (int cam_id = 0; cam_id < CamCtrls.Count; cam_id++)
                    {
                        for (int i = 0; i < ImageSynthesis.PASSNAMES.Length; i++)
                        {
                            if (imageSynthesis.Contains(ImageSynthesis.PASSNAMES[i]))
                            {
                                // Special case for optical flow camera - flow is really high whenver camera changes so it
                                // should just save black image
                                bool isOpticalFlow = (i == ImageSynthesis.PASS_NUM_OPTICAL_FLOW);
                                SaveRenderedFromCam(pathPrefix, i, isOpticalFlow, cam_id);
                            }
                        }
                        // Only if we want camera position for every frame
                        //cameraData.Add(new CameraData() { World2CamMatrix = CamCtrls[cam_id].CurrentCamera.worldToCameraMatrix, ProjectionMatrix = CamCtrls[cam_id].CurrentCamera.projectionMatrix, FrameStart = frameNum > 0 ? frameNum + 1 : 0 });
                    }
                    if (savePoseData) {
                        UpdatePoseData(frameNum);
                    }
                    if (saveSceneStates) {
                        sceneStateSequence.SetFrameNum(frameNum);
                    }
                }

                for (int cam_id = 0; cam_id < CamCtrls.Count; cam_id++)
                {
                    if (CamCtrls[cam_id] != null)
                        CamCtrls[cam_id].Update();
                }
                frameNum++;
                currentframeNum++;
            }
            // If code reaches here, it means either recording is set to false or
            // frameNum exceeded max frame number. If recording is still true,
            // it means max frame number is reached.
            if (recording) {
                Error = new ExecutionError($"Max frame number exceeded {MaxFrameNumber}");
            }
        }

        void SaveRenderedFromCam(string pathPrefix, int camPassNo, bool isOpticalFlow, int cam_id=0) 
        {
            const int RT_DEPTH = 24;

            Camera cam = CamCtrls[cam_id].CurrentCamera.GetComponent<ImageSynthesis>().m_captureCameras[camPassNo];
            if (cam == null)
            {
                return;
            }
            RenderTexture renderRT;
            // Use different render texture for depth values.
            if (camPassNo == ImageSynthesis.PASS_NUM_DEPTH) {
                // Half precision is good enough
                renderRT = RenderTexture.GetTemporary(ImageWidth, ImageHeight, RT_DEPTH, RenderTextureFormat.ARGBHalf);
            } else {
                renderRT = RenderTexture.GetTemporary(ImageWidth, ImageHeight, RT_DEPTH);
            }
            RenderTexture prevCameraRT = cam.targetTexture;

            // Render to offscreen texture (readonly from CPU side)
            cam.targetTexture = renderRT;
            cam.Render();   // <====== yes, u render ....
            //Debug.Log("Render!!!");
            // just for count
            //int indexOfObject = 0;
            if(_calcRectALL == true & (frameNum % _per_frame) == 0)
            {
                _textTargetObject.text = " yes, i am at calc all !";
                _currentGraph = _currentGraphCreator.UpdateGraph(_transform);
                _textTargetObject.text = _currentGraph.nodes.Count.ToString();
                foreach(EnvironmentObject eo in _currentGraph.nodes)
                {
                    
                    _textTargetObject.text = eo.class_name;
                    //Debug.Log(" name = " + eo.transform.gameObject.name);
                    if(eo.class_name != "character")
                    {
                        //indexOfObject += 1;
                        //_textTargetObject.text = "count : " + indexOfObject.ToString();
                        //Debug.Log(" Not character name = " + eo.transform.gameObject.name + "  id = " + eo.id);
                        //if( eo.transform.gameObject.GetComponent<MeshFilter>() != null )
                        //{
                            //Debug.Log(" Not character name = " + eo.transform.gameObject.name + "  id = " + eo.id);
                            SetVisCheckGameObject(eo);
                            //CalcPositionTarget(cam);
                            if(_visTargetObject.target == null){
                                Debug.Log(" SetVisCheckGameObject = " + "null");
                            }
                            else
                            {
                                Debug.Log(" SetVisCheckGameObject = " + _visTargetObject.target.name);
                            }

                            if( _visTargetObject.target != null )
                            {
                                VisibleRect vrRect = CalcPositionTarget(cam);

                                VisibleObject vo = new VisibleObject();
                                vo.name = _visTargetObject.className;   //    _myStr = _className;// _targetGO.name;
                                vo.id = _visTargetObject.id;
                                vo.frameId = frameNum;
                                vo.predicate = _textAction.text;
                                if( vrRect.vis == true)
                                {
                                    vo.leftTop = new Vector2Int((int)vrRect.rect.xMin, (int)vrRect.rect.yMax);
                                    vo.rightBottom = new Vector2Int((int)vrRect.rect.xMax, (int)vrRect.rect.yMin);
                                    vo.visible = vrRect.vis;
                                }
                                else
                                {
                                    vo.leftTop = new Vector2Int(0, 0);
                                    vo.rightBottom = new Vector2Int(0, 0);
                                    vo.visible = vrRect.vis;
                                }
                                //indexOfObject += 1;
                                //_textTargetObject.text = "count : " + indexOfObject.ToString();
                                _textTargetObject.text = eo.class_name;
                                if(vo.visible == true)
                                    _vod.voList.Add(vo);
                            }
                           
                        //}

                    }
                }

            }


            // calc rect for object
            if (_calcRect == true)
            {
                Debug.Log("which room are you = " + cam.transform.parent.parent.name);
                //CalcPositionTarget(cam);
                VisibleRect vrRect = CalcPositionTarget(cam);

                VisibleObject vo = new VisibleObject();
                vo.name = _visTargetObject.className;   //    _myStr = _className;// _targetGO.name;
                vo.id = _visTargetObject.id;
                vo.frameId = frameNum;
                vo.predicate = _textAction.text;
                if( vrRect.vis == true)
                {
                    vo.leftTop = new Vector2Int((int)vrRect.rect.xMin, (int)vrRect.rect.yMax);
                    vo.rightBottom = new Vector2Int((int)vrRect.rect.xMax, (int)vrRect.rect.yMin);
                    vo.visible = vrRect.vis;
                }
                else
                {
                    vo.leftTop = new Vector2Int(0, 0);
                    vo.rightBottom = new Vector2Int(0, 0);
                    vo.visible = vrRect.vis;
                }

                _textTargetObject.color = Color.green;
                _textTargetObject.text = vo.name;
                _vod.voList.Add(vo);

            }

            // calc rect for character
            if(_calcRectChar == true)
            {
                VisibleObject vo = new VisibleObject();
                vo.name = _characters[0].gameObject.name;
                VisibleRect vrRect = CalcPositionCharTarget(cam);

                vo.id = 1;
                vo.frameId = frameNum;
                vo.predicate = _textAction.text;
                if( vrRect.vis == true)
                {
                    vo.leftTop = new Vector2Int((int)vrRect.rect.xMin, (int)vrRect.rect.yMax);
                    vo.rightBottom = new Vector2Int((int)vrRect.rect.xMax, (int)vrRect.rect.yMin);
                    vo.visible = vrRect.vis;
                }
                else
                {
                    vo.leftTop = new Vector2Int(0, 0);
                    vo.rightBottom = new Vector2Int(0, 0);
                    vo.visible = vrRect.vis;
                }
                
                _vod.voList.Add(vo);
               
            }

            if(_calcRect == true | _calcRectChar == true)
            {
                if( (frameNum % _per_frame) == 0)
                {
                    string jsonstring = JsonUtility.ToJson(_vod);
                    string vofilePath = string.Format("{0}{1:D4}_{2}", pathPrefix, frameNum, cam_id) + "_2D.json";
                
                
                    using(StreamWriter sw = new StreamWriter(vofilePath, true, System.Text.Encoding.GetEncoding("UTF-8")))
                    {   
                        try
                        {
                            sw.Write(jsonstring);
                        }
                        catch(Exception e)
                        {
                            Debug.Log(e);
                        }
                    }
                }
                
                

                _vod.voList.Clear();
            }


            cam.targetTexture = prevCameraRT;

            Texture2D tex;
            //Texture2D texPNG;
            if (camPassNo == ImageSynthesis.PASS_NUM_DEPTH) {
                tex = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGBAHalf, false);
            } else {
                tex = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
            }

            // Corner case for optical flow - just render black texture
            if (isOpticalFlow && isCameraChanged) {
                // Set texture black
                for (int y = 0; y < ImageHeight; y++) {
                    for (int x = 0; x < ImageWidth; x++) {
                        tex.SetPixel(x, y, Color.black);
                    }
                }
                tex.Apply();
            } else {
                RenderTexture prevActiveRT = RenderTexture.active;
                RenderTexture.active = renderRT;
                // read offsreen texture contents into the CPU readable texture
                tex.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
                RenderTexture.active = prevActiveRT;
            }

            RenderTexture.ReleaseTemporary(renderRT);

            string filePath = string.Format("{0}{1:D4}_{3}_{2}", pathPrefix, frameNum,
                        ImageSynthesis.PASSNAMES[camPassNo], cam_id);

            
            byte[] bytes;
            // encode texture
            if (camPassNo == ImageSynthesis.PASS_NUM_DEPTH) {
                filePath += ".exr";
                bytes = tex.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            } else {
                filePath += ".png";
                bytes = tex.EncodeToPNG();
            }

            if(_withRect == false)
            {
                File.WriteAllBytes(filePath, bytes);
            }
            else
            {
                //string tempPath = Application.dataPath;
                //string path = Application.dataPath.Substring(0, Application.dataPath.Length - 6);
                ScreenCapture.CaptureScreenshot(filePath);
            }
            
            // Add 2022 out put grap per frame...
            if(_outGraph == true)
            {
                if( (frameNum % _per_frame) == 0)
                {
                    string filePathGraph = string.Format("{0}{1:D4}_{2}", pathPrefix, frameNum, cam_id) + "_graph.json";
                    //UpdateCharacterOfGraph();
                    _currentGraph = _currentGraphCreator.UpdateGraph(_transform);
                    UpDateGraphNodeState(); // update node state
                    UpDateGrabbedEdge();    // update edge sate
                    string jsonstringGraph = JsonConvert.SerializeObject(_currentGraph);

                    using(StreamWriter sw = new StreamWriter(filePathGraph, true, System.Text.Encoding.GetEncoding("UTF-8")))
                    {
                        try
                        {
                            sw.Write(jsonstringGraph);
                        }
                        catch(Exception e)
                        {
                            Debug.Log(e);
                        }
                    }
                }
            }

            // Reset the value - check if current check is on optical flow
            // since it's is the last GT we are rendering.
            if (isCameraChanged && isOpticalFlow) {
                isCameraChanged = false;
            }
        }

        public void CreateTextualGTs()
        {
            const string PREFIX_ACTION = "ftaa_";
            const string PREFIX_CAMERA = "cd_";
            const string PREFIX_POSE = "pd_";
            const string PREFIX_SCENE_STATE = "ss_";
            const string FILE_EXT_TXT = ".txt";
            const string FILE_EXT_JSON = ".json";

            string currentFileName = Path.Combine(OutputDirectory, PREFIX_ACTION) + FileName + FILE_EXT_TXT;

            if (actionRanges.Count == 0) {
                File.Delete(currentFileName);
            } else {
                using (StreamWriter sw = new StreamWriter(currentFileName)) {
                    foreach (ActionRange ar in actionRanges) {
                        sw.WriteLine(ar.GetString());
                    }
                }
            }

            currentFileName = Path.Combine(OutputDirectory, PREFIX_CAMERA) + FileName + FILE_EXT_TXT;

            if (cameraData.Count == 0) {
                File.Delete(currentFileName);
            } else {
                using (StreamWriter sw = new StreamWriter(currentFileName)) {
                    foreach (CameraData cd in cameraData) {
                        sw.WriteLine(cd.ToString());
                    }
                }
            }

            currentFileName = Path.Combine(OutputDirectory, PREFIX_POSE) + FileName + FILE_EXT_TXT;

            if (poseData.Count == 0) {
                File.Delete(currentFileName);
            } else {
                using (StreamWriter sw = new StreamWriter(currentFileName)) {
                    sw.WriteLine(PoseData.BoneNamesToString());
                    foreach (PoseData pd in poseData) {
                        sw.WriteLine(pd.ToString());
                    }
                }
            }

            currentFileName = Path.Combine(OutputDirectory, PREFIX_SCENE_STATE) + FileName + FILE_EXT_JSON;

            if (sceneStateSequence.states.Count == 0) {
                File.Delete(currentFileName);
            } else {
                using (StreamWriter sw = new StreamWriter(currentFileName)) {
                    sw.WriteLine(JsonUtility.ToJson(sceneStateSequence, true));
                }
            }
        }

        // ======================================================================================== //
        // =================================== Methods - Misc. ==================================== //
        // ======================================================================================== //        

        public bool BreakExecution()
        {
            return recording && (Error != null);
        }

        void UpdatePoseData(int actualFrameNum)
        {
            if (Animator != null)
                poseData.Add(new PoseData(actualFrameNum, Animator));
        }

        public void SetEnvironmentGraph(EnvironmentGraph eg)
        {
            _currentGraph = eg;
        }

        public void SetTransfrom(Transform t)
        {
            _transform = t;
        }

        public void SetEnvironmentGraphCreator(EnvironmentGraphCreator egc)
        {
            _currentGraphCreator = egc;
            _currentGraph = _currentGraphCreator.UpdateGraph(_transform);
        }

        public void SetCharcters(List<CharacterControl> cc)
        {
            _characters = cc;
            foreach(CharacterControl charctrl in cc)
            {
                Debug.Log("Character nama = " + charctrl.gameObject.name);
            }
            //Debug.Log("Amounts of charactors = " + _characters.Count);
        }

        // for the target object visiblity. set target evironment object here....
        public void SetVisCheckGameObject(EnvironmentObject eo)//string class_name, int id)
        {

            //bool canTrace = false;
            /*
            if(eo != null)
            {
                //_targetEO = eo;

                _visTargetObject.target = eo.transform.gameObject;

                if(_visTargetObject.target != null & eo.id != -1)
                {   
                    Debug.Log(" eo.id = " + eo.id + "  eo.transform.gameobject.name = " + eo.transform.gameObject.name );
                    //_visTargetObject.renderer[0] = _visTargetObject.target.GetComponent<Renderer>();
                    _visTargetObject.className = eo.class_name;
                    _visTargetObject.id = eo.id;

                    // for check target object by change material of target.....
                
                    //Shader shader = _targetGO.GetComponent<MeshRenderer>().material.shader;
                    //Material mt_red = new Material(shader);
                    //mt_red.color = Color.red;
                    //_targetGO.GetComponent<MeshRenderer>().material = mt_red;
                    
                }
                
                // and more...
                if(eo.category == "Rooms")
                {
                    _visTargetObject.target = null;
                    //_visTargetObject.renderer = null;
                    _visTargetObject.className = "Rooms";
                    _visTargetObject.id = -1;
                }

                if(eo.category == "Walls")
                {
                    _visTargetObject.target = null;
                    //_visTargetObject.renderer = null;
                    _visTargetObject.className = "Walls";
                    _visTargetObject.id = -1;
                }

                if(eo.category == "Ceiling")
                {
                    _visTargetObject.target = null;
                    //_visTargetObject.renderer = null;
                    _visTargetObject.className = "Ceiling";
                    _visTargetObject.id = -1;
                }

                if(eo.category == "Floor")
                {
                    _visTargetObject.target = null;
                    //_visTargetObject.renderer = null;
                    _visTargetObject.className = "Floor";
                    _visTargetObject.id = -1;
                }
                
            }
            else
            {
                _visTargetObject.target = null;
                //_visTargetObject.renderer = null;
                _visTargetObject.className = "non";
                _visTargetObject.id = -1;
            }
            */

            if(eo != null)
            {
                _visTargetObject.target = eo.transform.gameObject;
                switch(eo.category)
                {
                    case "Rooms":
                        _visTargetObject.target = null;
                        //_visTargetObject.renderer = null;
                        _visTargetObject.className = "Rooms";
                        _visTargetObject.id = -1;
                        break;
                    case "Walls":
                        _visTargetObject.target = null;
                        //_visTargetObject.renderer = null;
                        _visTargetObject.className = "Walls";
                        _visTargetObject.id = -1;
                        break;
                    case "Ceiling":
                        _visTargetObject.target = null;
                        //_visTargetObject.renderer = null;
                        _visTargetObject.className = "Ceiling";
                        _visTargetObject.id = -1;
                        break;
                    case "Floor":
                        _visTargetObject.target = null;
                        //_visTargetObject.renderer = null;
                        _visTargetObject.className = "Flioor";
                        _visTargetObject.id = -1;
                        break;
                    default:
                        if(_visTargetObject.target != null & eo.id != -1)
                        {   
                            Debug.Log(" eo.id = " + eo.id + "  eo.transform.gameobject.name = " + eo.transform.gameObject.name );
                            //_visTargetObject.renderer[0] = _visTargetObject.target.GetComponent<Renderer>();
                            _visTargetObject.className = eo.class_name;
                            _visTargetObject.id = eo.id;
                            //canTrace = true;
                            if(_visTargetObject.target.name == "kitchen_cabinet")
                            {
                                // special case kithen_cabinet
                                _visTargetObject.target = null;
                                //_visTargetObject.renderer = null;
                                _visTargetObject.className = "kitchen_cabinet";
                                _visTargetObject.id = -1;
                            }
                    
                        }
                        break;
                 }
            }
            
            //_calcRect = true;
        }


        public void PleaseCallMe(string str)
        {
            Debug.Log("I called.... " + str);
        }

        public void ActivateObjectNow(string str)
        {
            Debug.Log("ActivateObjectNow Name = " + str);
            _canObjectStateUpdate = true;
            if(_os == Utilities.ObjectState.GRABED)
            {
                _canGrabbedEdgeUpdata = true;
                _HandGrabbed = str;
                Debug.Log("_canGrabbedEdgeUpdata True !!! Hand = " + _HandGrabbed);
            }
            else
            {
                _canGrabbedEdgeUpdata = false;
            }
        }

        // Update node state of graph, I think it no needed anymore
        public void SetObjectStateOfGraph(string eoName, int eoId, Utilities.ObjectState os)
        {
            Debug.Log("_eoName = " + eoName);
            _eoName = eoName;
            _eoId = eoId;
            _os = os;
            
        }

        private void UpDateGrabbedEdge()
        {

            
            if(_canGrabbedEdgeUpdata == true)
            {
                Debug.Log("Now UpDateGrabbedEdge...");
                
                EnvironmentObject grabbed_obj;
                foreach(EnvironmentObject eo in _currentGraph.nodes)
                {
                    if(eo.class_name == _eoName && eo.id == _eoId)
                    {
                        grabbed_obj = eo;
                        Debug.Log(" I found grabbed_obj at UpdateGraphEdge");
                        foreach (KeyValuePair<EnvironmentObject, Character> o in _currentGraphCreator.characters)
                        {
                            Debug.Log("at UpDateGrabbedEdge character name = " + o.Value.character.prefab_name);
                            //Debug.Log("UpdateGraphEdges with Null if Grab action" );
                            if(_HandGrabbed == "LeftHand")
                            {
                                _currentGraphCreator.RemoveGraphEdgesWithObject(grabbed_obj);
                                _currentGraphCreator.AddGraphEdge(o.Value.character, grabbed_obj, ObjectRelation.HOLDS_LH);
                                Debug.Log("Recorder Left hand Grabbed_obj = " + grabbed_obj.prefab_name);
                                break;
                            }
                            
                            if(_HandGrabbed == "RightHand")
                            {
                                _currentGraphCreator.RemoveGraphEdgesWithObject(grabbed_obj);
                                _currentGraphCreator.AddGraphEdge(o.Value.character, grabbed_obj, ObjectRelation.HOLDS_RH);
                                Debug.Log("Recorder Right hand Grabbed_obj = " + grabbed_obj.prefab_name);
                                break;
                            }
                        }
                        
                    }
                }

                //_canGrabbedEdgeUpdata = false;
            }
        }

        private void UpDateGraphNodeState()
        {
            if(_canObjectStateUpdate == true)
            {
                foreach(EnvironmentObject eo in _currentGraph.nodes)
                {
                    if(eo.class_name == _eoName && eo.id == _eoId)
                    {   
                        eo.states.Clear();
                        eo.states.Add(_os);
                        Debug.Log(" I set object state as " + _os.ToString() + " At Frame No = " + frameNum);
                        _canObjectStateUpdate = false;
                        break;
                    
                    }
                }
            }
        }
        // Update character state of graph, I think it no needed anymore
      
        // Update character state of graph, I think it no needed anymore

        // Add 2022 for checking a object positon with screen coordinate

        private VisibleRect GUI2Rect(Camera cam, GameObject target, bool human)
        {
            if(target.GetComponent<Renderer>() != null)
            {
                if (GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), target.GetComponent<Renderer>().bounds) == false)
                {
                    return GetNoVisRect();
                }
            }
            else{
                return GetNoVisRect();
            }
            

            // Add Oct/2022
            // but not use...
            /*
            BoxCollider[] bc = target.transform.GetComponentsInChildren<BoxCollider>();
            if( bc.Length != 0)
            {
                foreach(BoxCollider b in bc)
                {
                    b.enabled = true;
                }
            }
            */
            

            Vector3[] vertices;
           
            if(human == false)
            {
                vertices = target.GetComponent<MeshFilter>().mesh.vertices;
            }
            else
            {
                // you need bake a mesh to get snapshot of current Mesh state....
                Mesh mesh = new Mesh();
                SkinnedMeshRenderer smr = target.GetComponent<SkinnedMeshRenderer>();
                smr.BakeMesh(mesh, true);
                vertices = mesh.vertices;
            }

            
            //
            //  Check target be visible by camera or not... Add 2022 
            //
            RaycastHit hit;
            bool targetVisible = false;
            foreach(Vector3 pos in vertices)
            {

                if(human == true)
                {
                    if(Physics.Raycast(cam.transform.position, (target.transform.TransformPoint(pos) - cam.transform.position).normalized, out hit, 40.0f))
                    {
                    // check name of gameobject here...
                        /*
                        if(hit.transform.parent.name == target.name || hit.transform.name == target.name)
                        {
                            //Debug.Log("I found character body parts");
                            Debug.Log("Hit humna hit name = " + hit.transform.name + "  taget,name = " + target.name + "  frameNum = " + frameNum);
                            targetVisible = true;
                            break;
                        }
                        else
                        {
                            Debug.Log("No Hit humna hit name = " + hit.transform.name + "  taget,name = " + target.name + "  frameNum = " + frameNum);
                        }
                        */
                        //Debug.Log("Hit humna hit name = " + hit.transform.name + "  taget,name = " + target.name + "  frameNum = " + frameNum);
                        targetVisible = true;
                        break;
                    
                     }
                }
                else
                {
                    //string hitName = Physics.RaycastAll(cam.transform.position, (target.transform.TransformPoint(pos) - cam.transform.position).normalized).Last().transform.name;
                    // check name of gameobject here...
                    if(Physics.Raycast(cam.transform.position, (target.transform.TransformPoint(pos) - cam.transform.position).normalized, out hit, 40.0f))
                    {
                        //_textTargetObject.text = hitName;   
                        Transform[] transforms = _visTargetObject.target.transform.GetComponentsInChildren<Transform>();
                        foreach(Transform transform in transforms)
                        {
                            if(transform.name == hit.transform.name)
                            {
                                //Debug.Log("hit name = " + hit.transform.name + "  hierarchy name = " + transform.name + "  target name = " + target.name);
                                targetVisible = true;
                                break;
                            }
                        }
                    }
                  
                }
            }

            //
            //  target is not visible by camera go back with NoVisibleRect...
            //
            if(targetVisible == false)
            {
                
                return GetNoVisRect();
            }



            float x1 = float.MaxValue, y1 = float.MaxValue, x2 = float.MinValue, y2 = float.MinValue;
            foreach (Vector3 vert in vertices)
            {
                //Vector2 tmp = WorldToGUIPoint(cam, target.transform.TransformPoint(vert));
                Vector2 tmp = cam.WorldToScreenPoint(target.transform.TransformPoint(vert));
                if (tmp.x < x1) x1 = tmp.x;
                if (tmp.x > x2) x2 = tmp.x;
                if (tmp.y < y1) y1 = tmp.y;
                if (tmp.y > y2) y2 = tmp.y;
            }

            VisibleRect vr = new VisibleRect();
            vr.rect = new Rect(x1, y1, x2 - x1, y2 - y1);
            vr.color = Color.green;
            vr.vis = true;

            vr = CheckVisRect(vr);

            return vr;
        }


        private VisibleRect CheckVisRect(VisibleRect vr)
        {
            VisibleRect visRect = new VisibleRect();
            visRect.rect = vr.rect;
            visRect.vis = vr.vis;
            visRect.color = vr.color;


            if(vr.rect.xMax < 0.0f || vr.rect.yMax < 0.0f)
            {
                 return GetNoVisRect();
            }

            if(vr.rect.xMin > (float)ImageWidth || vr.rect.yMin > (float)ImageHeight)
            {
                return GetNoVisRect();
            }

            float xMin = vr.rect.xMin;
            float yMin = vr.rect.yMin;
            float widht = vr.rect.width;
            float height = vr.rect.height;
            bool bChange = false;
            if(vr.rect.xMin < 0.0f )
            {   
                bChange = true;
                xMin = _space;
                widht = vr.rect.width + vr.rect.xMin;
            }

            if(vr.rect.xMax > (float)ImageWidth)
            {       
                bChange = true;
                widht = (float)ImageWidth - xMin - _space;
            }

            if(vr.rect.yMin < 0.0f)
            {
                bChange = true;
                yMin = _space;
                height = vr.rect.height + vr.rect.yMin;
            }

            if(vr.rect.yMax > (float)ImageHeight)
            {
                bChange = true;
                height = (float)ImageHeight - yMin - _space;
            }



            if(bChange == true)
            {
                visRect.rect = new Rect(xMin, yMin, widht, height);
                //visRect.color = Color.green;
                //visRect.vis = true;
            }

            return visRect;

        }

        //
        // return no valid rect...
        private VisibleRect GetNoVisRect()
        {
            VisibleRect vr = new VisibleRect();
            vr.rect = new Rect(0f, 0f, ImageWidth, ImageHeight);
            vr.vis = false;
            vr.color = Color.red;

            return vr;

        }
    

        //
        //  for object
        //
        private VisibleRect CalcPositionTarget(Camera cam)
        {
            //_youcansee = false;

            //bool canyouseeme;

            VisibleRect vrRect = new VisibleRect();
            List<GameObject> targetGOS = new List<GameObject>();
            //Debug.Log("_visTargetObject.id = " + _visTargetObject.id.ToString());

            if(_visTargetObject.id  > 0)
            {
                Transform[] transforms = _visTargetObject.target.transform.GetComponentsInChildren<Transform>();
                //List<GameObject> targetGOS = new List<GameObject>();
                //targetGOS = FindGOSfromGObject(_visTargetObject.target);
                foreach(Transform transform in transforms)
                {
                    if (!targetGOS.Contains(transform.gameObject))
                        {
                            targetGOS.Add(transform.gameObject);
                        }
                }
            }
            else
            {
                _visTargetObject.id = -1;
                _textTargetObject.text = " Not defined Index...";
            }

            if(_visTargetObject.id == -1)
            {
                vrRect =  GetNoVisRect();
            }
            else
            {
                vrRect = GetObjectRect(cam, targetGOS);  //GUI2Rect(cam, _visTargetObject.target, false);
            }

            SetScreenRectObject(vrRect, _visTargetObject.className);

            return vrRect;

            //Debug.Log("Calc It !!!!");
        }
        
        //
        //  for character
        //
        private VisibleRect CalcPositionCharTarget(Camera cam)
        {
            //Rect rect = new Rect();
            VisibleRect vrRect = new VisibleRect();

            //GameObject targetGO = FindGOfromChar();
            List<GameObject> targetGOS = new List<GameObject>();
            targetGOS = FindGOSfromChar();
            //Debug.Log("Character name in recorder = " + targetGOS[0].name + " amounts = " + targetGOS.Count);
            if(targetGOS != null)  
            { 
                vrRect = GetCharRect(cam, targetGOS);
            }
            else
            {
                vrRect = GetNoVisRect();
            }
            
            SetScreenRectCharacter(vrRect, _characters[0].gameObject.name);

            return vrRect;

        }



        private void SetScreenRectObject(VisibleRect vr, string name)
        {
            //Vector3 pos = _rectTransformObject.anchoredPosition;
            //pos.x = vr.rect.center.x;
            //pos.y = vr.rect.center.y;
            //Vector3 pos = new Vector3(vr.rect.center.x, vr.rect.center.y, 0f);

            
            //_rectTransformObject.anchoredPosition = pos;
            _rectTransformObject.transform.position = new Vector3(vr.rect.center.x, vr.rect.center.y, 0f);;

            //_rectTransformObject.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, vr.rect.size.x + _space);
            //_rectTransformObject.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, vr.rect.size.y + _space);
            _rectTransformObject.sizeDelta = new Vector2(vr.rect.width, vr.rect.height);

            _RectUIObject.GetComponent<Image>().color = _textObject.color = vr.color;

            _textObject.text = "LeftTop = " + ((int)(vr.rect.xMin)).ToString("0000") + " , " + ((int)(vr.rect.yMax)).ToString("0000") + 
                         "  RightBottom = " +((int)( vr.rect.xMax)).ToString("0000") + " , " + ((int)(vr.rect.yMin)).ToString("0000") + 
                         "   "  + name;
        }

        private void SetScreenRectCharacter(VisibleRect vr, string name)
        {
            //Vector3 pos = _rectTransformCharacter.anchoredPosition;
            //pos.x = vr.rect.center.x;
            //pos.y = vr.rect.center.y;
            //Vector3 pos = new Vector3(vr.rect.center.x, vr.rect.center.y, 0f);

            //_rectTransformCharacter.anchoredPosition = pos;
            _rectTransformCharacter.transform.position = new Vector3(vr.rect.center.x, vr.rect.center.y, 0f);;

            //_rectTransformCharacter.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, vr.rect.size.x + _space);
            //_rectTransformCharacter.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, vr.rect.size.y + _space);
            _rectTransformCharacter.sizeDelta = new Vector2(vr.rect.width, vr.rect.height);

            _RectUICharacter.GetComponent<Image>().color = _textCharacter.color = vr.color;

            _textCharacter.text = "LeftTop = " + ((int)(vr.rect.xMin)).ToString("0000") + " , " + ((int)(vr.rect.yMax)).ToString("0000") + 
                         "  RightBottom = " +((int)( vr.rect.xMax)).ToString("0000") + " , " + ((int)(vr.rect.yMin)).ToString("0000") + 
                         "   "  + name;
        }

        private VisibleRect GetObjectRect(Camera cam, List<GameObject> tgo)
        {
            VisibleRect vrOri = new VisibleRect();
            vrOri.vis = false;
            vrOri.color = Color.red;
            bool can = false;

            // check can be watched first
            foreach(GameObject go in tgo)
            {
                //rect = GUI2dRectWithObject(cam, go, true, out can);
                vrOri = GUI2Rect(cam, go, false);   // false = object
                if(vrOri.vis == true) {
                    can = true;
                    break;
                }
                
            }
            
            if(can == false) 
            {
                //canyouseeme = false;
                //return GetNoVisibleRect();

                return GetNoVisRect();

            }
                
            VisibleRect vr = new VisibleRect();
            foreach(GameObject go in tgo)
            {
                //tr = GUI2dRectWithObject(cam, go, true, out can);
                vr = GUI2Rect(cam, go, false);
                if(vr.vis == true)
                {
                    if(vr.rect.xMin < vrOri.rect.xMin)
                    {
                        vrOri.rect.xMin = vr.rect.xMin;
                    }

                    if(vr.rect.xMax > vrOri.rect.xMax)
                    {
                        vrOri.rect.xMax = vr.rect.xMax;
                    }

                    if(vr.rect.yMin < vrOri.rect.yMin)
                    {
                        vrOri.rect.yMin = vr.rect.yMin;
                    }

                    if(vr.rect.yMax > vrOri.rect.yMax)
                    {
                        vrOri.rect.yMax = vr.rect.yMax;
                    }
                }

            }

            return  vrOri;
        }

        private VisibleRect GetCharRect(Camera cam, List<GameObject> tgo)
        {
            VisibleRect vrOri = new VisibleRect();
            vrOri.vis = false;
            vrOri.color = Color.red;
            bool can = false;
            foreach(GameObject go in tgo)
            {
                //rect = GUI2dRectWithObject(cam, go, true, out can);
                vrOri = GUI2Rect(cam, go, true);    // true = charactor
                if(vrOri.vis == true) {
                    can = true;
                    break;
                }
                
            }
            
            if(can == false) 
            {
                //canyouseeme = false;
                //return GetNoVisibleRect();

                return GetNoVisRect();

            }
                
            VisibleRect vr = new VisibleRect();
            foreach(GameObject go in tgo)
            {
                //tr = GUI2dRectWithObject(cam, go, true, out can);
                vr = GUI2Rect(cam, go, true);
                if(vr.vis == true)
                {
                    if(vr.rect.xMin < vrOri.rect.xMin)
                    {
                        vrOri.rect.xMin = vr.rect.xMin;
                    }

                    if(vr.rect.xMax > vrOri.rect.xMax)
                    {
                        vrOri.rect.xMax = vr.rect.xMax;
                    }

                    if(vr.rect.yMin < vrOri.rect.yMin)
                    {
                        vrOri.rect.yMin = vr.rect.yMin;
                    }

                    if(vr.rect.yMax > vrOri.rect.yMax)
                    {
                        vrOri.rect.yMax = vr.rect.yMax;
                    }
                }

            }

            return  vrOri;

        }
        
        // no need any more
        private List<GameObject> FindGOSfromGObject(GameObject root)
        {
            //
            // gets all parts of MeshRenderer componet....
            //

            List<GameObject> partObject = new List<GameObject>();

            if(root.GetComponent<MeshRenderer>() != null)
            {
                partObject.Add(root);
            }

            GetChildren(root, partObject);
            /*
            foreach(GameObject go in partObject)
            {
                Debug.Log(" partObject = " + go.name);
            }
            */

            return partObject;
        }

        private void GetChildren(GameObject obj, List<GameObject> golist) {
            Transform children = obj.GetComponentInChildren < Transform > ();
            // no children, over
            if (children.childCount == 0) {
                return;
            }
            foreach(Transform ob in children) {
                if (ob.gameObject.GetComponent<Renderer>() != null)
                {
                    if (!golist.Contains(ob.gameObject))
                    {
                        golist.Add(ob.gameObject);//list.Add(num);
                    }
                }
                GetChildren(ob.gameObject, golist);
            }
        }




        private List<GameObject> FindGOSfromChar()
        {

            //
            // gets all parts of character have SkinMeshRenderer componet....
            //

            if(_characters != null)
            {

                List<GameObject> partChar = new List<GameObject>();

                foreach (Transform ct in _characters[0].gameObject.transform)
                {
                    //Debug.Log(ct.gameObject.name);
                    if(ct.gameObject.GetComponent<SkinnedMeshRenderer>() != null )
                    partChar.Add(ct.gameObject);

                    foreach ( Transform child in ct )
                    {
                        if(child.gameObject.GetComponent<SkinnedMeshRenderer>() != null )
                        partChar.Add(child.gameObject);
                    }

                }

                return partChar;
            
            }
            else
            {
                return null;
            }

        }

        private GameObject FindGOfromChar()
        {
            if(_characters != null)
            {

                List<GameObject> partChar = new List<GameObject>();

                foreach (Transform ct in _characters[0].gameObject.transform)
                {
                    //Debug.Log(ct.gameObject.name);
                    partChar.Add(ct.gameObject);
                }

                return partChar[0]; // body
            
            }
            else
            {
                return null;
            }


        }

        private bool SeekTargt(GameObject target, Camera cam)
        {
            bool getIt = false;

            //
            // Check gameobject is inside of frustum
            //
            if (GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(cam), target.GetComponent<Renderer>().bounds) == true)
            {
               Vector3[] vertices = target.GetComponent<MeshFilter>().mesh.vertices;

                //
                //  Check target be visible by camera or not... Add 2022 
                //
                RaycastHit hit;
                foreach(Vector3 pos in vertices)
                {
                    if(Physics.Raycast(cam.transform.position, (target.transform.TransformPoint(pos) - cam.transform.position).normalized, out hit, 10.0f))
                    {
                        // check name of gameobject here...
                        if(hit.transform.parent.name == target.name || hit.transform.parent.parent.name == target.name)
                        {
                            //Debug.Log("parent name = " + hit.transform.parent.name + "  name = " + hit.transform.name);
                            getIt = true;
                            break;
                        }  
                    }
                }                

            }

            return getIt;
        }

      


    }
}
