﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using StoryGenerator;
using StoryGenerator.RoomProperties;
using StoryGenerator.ChairProperties;
using StoryGenerator.CharInteraction;
using StoryGenerator.Recording;
using StoryGenerator.Rendering;
using StoryGenerator.SceneState;
using System.Text.RegularExpressions;
using RootMotion.FinalIK;
using StoryGenerator.Scripts;
using StoryGenerator.Utilities;
using System.Text;
using StoryGenerator.DoorProperties;
using System.Threading;


namespace StoryGenerator.Utilities
{
    public class ActionObjectData
    {
        public Character character;
        public ScriptPair script;
        public IDictionary<Tuple<string, int>, ScriptObjectData> dict_script;

        public ActionObjectData(Character character, ScriptPair script, IDictionary<Tuple<string, int>, ScriptObjectData> dict_script)
        {
            this.character = character;
            this.script = script;
            this.dict_script = dict_script;
        }

        public bool GetFirstObject(out ScriptObjectData first_obj)
        {
            ScriptObjectName obj1 = script.Action.Name;
            return dict_script.TryGetValue(new Tuple<string, int>(obj1.Name, obj1.Instance), out first_obj);
        }

        public bool GetSecondObject(out ScriptObjectData second_obj)
        {
            ScriptObjectName? obj2 = null;
            if (script.Action is PutAction)
                obj2 = ((PutAction)script.Action).DestName;

            if (!obj2.HasValue)
            {
                second_obj = null;
                return false;
            }
            return dict_script.TryGetValue(new Tuple<string, int>(((ScriptObjectName)obj2).Name, ((ScriptObjectName)obj2).Instance), out second_obj);
        }
    }

    public class ObjectData
    {
        public GameObject GameObject { get; protected set; }
        public Vector3 Position { get; protected set; }       // Position of the object: can be GameObject.transform.position or new position if it was moved

        public ObjectData(GameObject gameObject, Vector3 position)
        {
            GameObject = gameObject;
            Position = position;
        }

    }

    public enum OpenStatus
    {
        UNKNOWN,    // Not known if openable
        OPEN,
        CLOSED
    }

    public class ScriptObjectData : ObjectData
    {
        public bool Grabbed { get; private set; }                   // true if object is in one of the hands, in this case Position property is position from where it was grabbed
        public OpenStatus OpenStatus { get; private set; }          // open/closed or unknown (status is unknown until it is not important to know the status)
        public Vector3 InteractionPosition { get; private set; }    // Interaction position of character at most recent manipulation

        public ScriptObjectData(GameObject gameObject, Vector3 position, Vector3 interactionPosition, bool grabbed, OpenStatus openStatus) :
            base(gameObject, position)
        {
            InteractionPosition = interactionPosition;
            Grabbed = grabbed;
            OpenStatus = openStatus;
        }
    }

    public class ProcessingReport
    {
        private List<string> messageList = new List<string>();
        private List<string> processingMessageList = new List<string>();

        public ProcessingReport()
        {
        }

        public bool Success { get { return messageList.Count == 0 && processingMessageList.Count == 0; } }

        public override string ToString()
        {
            if (Success) return "";
            else
            {
                StringBuilder sb = new StringBuilder();

                foreach (string msg in messageList)
                {
                    sb.Append(msg); sb.Append('\n');
                }
                foreach (string msg in processingMessageList)
                {
                    sb.Append(msg); sb.Append('\n');
                }
                return sb.ToString();
            }
        }

        public void Reset()
        {
            messageList.Clear();
            processingMessageList.Clear();
        }

        public void ResetProcessingMessage()
        {
            processingMessageList.Clear();
        }

        public void AddItem(string key, string value)
        {
            string msg = string.Format("{0}: {1}", key, value);

            if (!messageList.Contains(msg))
                messageList.Add(msg);
        }

        public void AddItem(string key, IEnumerable<string> value)
        {
            AddItem(key, string.Join(", ", value.ToArray()));
        }

        public void AddItem(string key, params string[] values)
        {
            AddItem(key, string.Join(", ", values));
        }

        public void AddProcessingItem(string key, string value)
        {
            processingMessageList.Add(string.Format("{0}: {1}", key, value));
        }

        public void AddProcessingItem(string key, params string[] values)
        {
            AddProcessingItem(key, string.Join(", ", values));
        }
    }

    #region Actions

    public class GotoAction : IAction
    {
        public IObjectSelector Selector { get; private set; }
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public InteractionType Intention { get; private set; }
        public bool Run { get; private set; }

        public GotoAction(int scriptLine, IObjectSelector selector, string name, int instance, bool run = false, InteractionType intention = InteractionType.UNSPECIFIED)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
            Intention = intention;
            Run = run;
        }
    }

    public class TurnAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }
        public InteractionType Intention { get; private set; }
        public float Degrees { get; private set; }
        public TurnAction(int scriptLine, float degrees, InteractionType intention = InteractionType.UNSPECIFIED)
        {
            ScriptLine = scriptLine;
            Intention = intention;
            Degrees = degrees;
        }
    }

    public class GoforwardAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }
        public InteractionType Intention { get; private set; }
        public bool Run { get; private set; }
        public GoforwardAction(int scriptLine, bool run = false, InteractionType intention = InteractionType.UNSPECIFIED)
        {
            ScriptLine = scriptLine;
            Intention = intention;
            Run = run;
        }
    }

    public class GotowardsAction : IAction
    {
        public IObjectSelector Selector { get; private set; }
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public InteractionType Intention { get; private set; }
        public bool Run { get; private set; }
        public float WalkDist { get; private set; }

        public GotowardsAction(int scriptLine, IObjectSelector selector, string name, int instance, bool run = false, InteractionType intention = InteractionType.UNSPECIFIED, float walk_dist = 1.0f)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
            Intention = intention;
            WalkDist = walk_dist;
            Run = run;
        }
    }



    public class WatchAction : IAction
    {
        public const float ShortDuration = 0.5f;
        public const float MediumDuration = 2.0f;
        public const float LongDuration = 6.0f;

        public IObjectSelector Selector { get; private set; }
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public float Duration { get; private set; }   // Duration in seconds

        public WatchAction(int scriptLine, IObjectSelector selector, string name, int instance, float duration)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
            Duration = duration;
        }
    }

    public class SitAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector { get; private set; }


        public SitAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class StandupAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public StandupAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }

    }

    public class GrabAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;

        public GrabAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class DrinkAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;


        public DrinkAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class PhoneAction : IAction
    {
        public enum PhoneActionType { TALK, TEXT };

        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public PhoneActionType ActionType { get; private set; }
        public IObjectSelector Selector;

        public PhoneAction(int scriptLine, IObjectSelector selector, string name, int instance, PhoneActionType actionType)
        {
            ScriptLine = scriptLine;
            Name = new ScriptObjectName(name, instance);
            ActionType = actionType;
            Selector = selector;
        }
    }

    public class TouchAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;

        public TouchAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            Selector = selector;
            ScriptLine = scriptLine;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class PutAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public ScriptObjectName DestName { get; private set; }
        public int ScriptLine { get; private set; }
        public bool PutInside { get; private set; }
        public IObjectSelector Selector;


        public PutAction(int scriptLine, IObjectSelector selector, string name, int instance, string destName, int destInstance, bool putInside)
        {
            ScriptLine = scriptLine;
            Name = new ScriptObjectName(name, instance);
            DestName = new ScriptObjectName(destName, destInstance);
            PutInside = putInside;
            Selector = selector;

        }
    }

    public class PutBackAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }

        public PutBackAction(int scriptLine, string name, int instance)
        {
            ScriptLine = scriptLine;
            Name = new ScriptObjectName(name, instance);
        }
    }


    // Switch on or off
    public class SwitchOnAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public bool Off { get; private set; }
        public IObjectSelector Selector;


        public SwitchOnAction(int scriptLine, IObjectSelector selector, string name, int instance, bool off)
        {
            ScriptLine = scriptLine;
            Name = new ScriptObjectName(name, instance);
            Off = off;
            Selector = selector;

        }
    }

    public class OpenAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public bool Close { get; private set; }
        public IObjectSelector Selector;


        public OpenAction(int scriptLine, IObjectSelector selector, string name, int instance, bool close)
        {
            Selector = selector;
            ScriptLine = scriptLine;
            Name = new ScriptObjectName(name, instance);
            Close = close;
        }
    }

    public class BrushAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public BrushAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class CutAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public CutAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class EatAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public EatAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class FoldAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public FoldAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class JumpAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public JumpAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class JumpUpAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public JumpUpAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class JumpDownAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public JumpDownAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class KneelAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public KneelAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class LiftAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public LiftAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class DropAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public DropAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class RinseAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public RinseAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class SquatAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public SquatAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class SqueezeAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public SqueezeAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class StretchAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public StretchAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class SweepAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;


        public SweepAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class PutOnAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public PutOnAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class PutOffAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public PutOffAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class StirAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public StirAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class ThrowAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public ThrowAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class TypeAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public TypeAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class UnFoldAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public UnFoldAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class VacuumAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public VacuumAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class WakeUpAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public WakeUpAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }
    
    public class WipeAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public ScriptObjectName WipeName { get; private set; }
        public IObjectSelector Selector;
        public bool Clean { get; private set; }


        public WipeAction(int scriptLine, IObjectSelector selector, string name, int instance, string wipeName, int wipeInstance, bool clean)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
            WipeName = new ScriptObjectName(wipeName, wipeInstance);
            Clean = clean;
        }

    }

    public class WrapAction : IAction
    {
        public ScriptObjectName Name { get; private set;}
        public IObjectSelector Selector;
        public int ScriptLine { get; private set; }

        public WrapAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class WriteAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public WriteAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class FallAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public FallAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class FallSitAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public FallSitAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class FallTableAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public FallTableAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class FallBackAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public FallBackAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class StandAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public StandAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class StraddleAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public StraddleAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }
    public class LegOppAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public LegOppAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class StandWithAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public StandWithAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }

    public class WalkWithAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public WalkWithAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }
    public class ScrubAction : IAction
    {
      public ScriptObjectName Name { get; private set; }
      public ScriptObjectName ScrubName { get; private set;}
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;


        public ScrubAction(int scriptLine, IObjectSelector selector, string name, int instance, string scrubName, int scrubInstance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
            ScrubName = new ScriptObjectName(scrubName, scrubInstance);
        }
    }
    public class SewAction : IAction
    {
        public ScriptObjectName Name { get { return new ScriptObjectName("", 0); } }
        public int ScriptLine { get; private set; }

        public SewAction(int scriptLine)
        {
            ScriptLine = scriptLine;
        }
    }
    public class ShakeAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;


        public ShakeAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }
    public class SmellAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;

        public SmellAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }
    public class SoakAction : IAction
    {
        public ScriptObjectName Name { get; private set; }
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;

        public SoakAction(int scriptLine, IObjectSelector selector, string name, int instance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
        }
    }

    public class PourAction : IAction
    {
        public ScriptObjectName Name { get; private set; }

        public ScriptObjectName PourName { get; private set;}
        public int ScriptLine { get; private set; }
        public IObjectSelector Selector;


        public PourAction(int scriptLine, IObjectSelector selector, string name, int instance, string pourName, int pourInstance)
        {
            ScriptLine = scriptLine;
            Selector = selector;
            Name = new ScriptObjectName(name, instance);
            PourName = new ScriptObjectName(pourName, pourInstance);
        }
    }


    #endregion

    public interface IStateGroup : IEnumerable<State>
    {
        int Count { get; }
        State Last();
    }


    /*
     *  State variables: 
     *  GOTO_SIT_LOOK_AT_OBJECT (GameObject)  set in goto action if next action is sit (direction to turn character to)
     *  GOTO_SIT_TARGET (GameObject)          set in goto action if next action is sit (positin to sit to)
     *  CHARACTER_STATE (string)              null if not specified (standing), "SITTING"         
     *  ROOM_CONSTRAINT (string)              canonical name of room we are allowed to enter in the next step, null if no restrictions
     *  RIGHT_HAND_OBJECT (GameObject)        object in right hand
     *  LEFT_HAND_OBJECT (GameObject)         object in left hand
     *  PUT_POSITION (Vector3)                where to put object
     *  TOUCH_POSITION (Vector3)              where to touch object
     *  
     *  Action flags:
     *  GOTO_TURN                             turn at the end of walk
     *  STANDUP                               stand up before walking
     */
    public class State : IStateGroup
    {
        public class ObjectPositionData
        {
            public bool grabbed;                  // true if object is in one of the hands
            public Vector3 position;              // Position of the object: current or from where it was grabbed (see grabbed field)
            public Vector3 interactionPosition;   // Interaction position of character at grab or put action (see grabbed field)

            public ObjectPositionData(bool grabbed, Vector3 position, Vector3 interactionPosition)
            {
                this.grabbed = grabbed;
                this.position = position;
                this.interactionPosition = interactionPosition;
            }

            public ObjectPositionData(Vector3 interactionPosition)
            {
                grabbed = true;
                this.interactionPosition = interactionPosition;
            }

            public ObjectPositionData(ObjectPositionData opd)
            {
                grabbed = opd.grabbed;
                position = opd.position;
                interactionPosition = opd.interactionPosition;
            }

        }

        public State Previous { get; private set; }                 // Prevoius state, null if first
        public IAction Action { get; set; }                         // Action which produced this state
        public Vector3 InteractionPosition { get; set; }            // Position of the character after execution of the action
        public Func<State, IEnumerator> ActionMethod { get; set; }  // Unity action executor (Walk to, Grab, etc.)
        private HashSet<string> actionFlags;                        // Action flags map, accessed via AddActionFlag and HasActionFlag methods
        private IDictionary<string, object> temporaryVariables;     // Variable map, accessed via AddTempAAAA and GetTempAAAA methods, not copied to the next state
        private IDictionary<string, object> variables;              // Variable map, accessed via AddAAAA and GetAAAA methods, copied to the next state
        public IDictionary<Tuple<string, int>, ScriptObjectData> scriptObjects;  // Map from name (= name, instance pair) to list of (instance number, game object) pairs
                                                                                 // Access with Get/AddScriptGameObject


        public State(Vector3 ip)
        {
            Previous = null;
            InteractionPosition = ip;
            ActionMethod = DummyActionMethod;
            actionFlags = new HashSet<string>();
            temporaryVariables = new Dictionary<string, object>();
            variables = new Dictionary<string, object>();
            scriptObjects = new Dictionary<Tuple<string, int>, ScriptObjectData>();
        }

        public State(State prev, Vector3 ip)
        {
            Previous = prev;
            InteractionPosition = ip;
            ActionMethod = DummyActionMethod;
            actionFlags = new HashSet<string>();
            temporaryVariables = new Dictionary<string, object>();
            variables = new Dictionary<string, object>(prev.variables);
            scriptObjects = new Dictionary<Tuple<string, int>, ScriptObjectData>(prev.scriptObjects);
        }

        public State(State prev, IAction action, Vector3 ip, Func<State, IEnumerator> am)
        {
            Previous = prev;
            Action = action;
            InteractionPosition = ip;
            ActionMethod = am;
            actionFlags = new HashSet<string>();
            temporaryVariables = new Dictionary<string, object>();
            variables = new Dictionary<string, object>(prev.variables);
            scriptObjects = new Dictionary<Tuple<string, int>, ScriptObjectData>(prev.scriptObjects);
        }

        public void AddActionFlag(string name)
        {
            actionFlags.Add(name);
        }

        public bool HasActionFlag(string name)
        {
            return actionFlags.Contains(name);
        }

        public void AddTempObject(string name, object o)
        {
            temporaryVariables[name] = o;
        }

        public object GetTempObject(string name)
        {
            object o;

            if (temporaryVariables.TryGetValue(name, out o)) return o; else return null;
        }

        public IEnumerable<T> GetTempEnumerable<T>(string name)
        {
            return (IEnumerable<T>)GetTempObject(name) ?? new T[] { };
        }

        public IEnumerable<T> GetTempEnumerable<T>(string name, T defVal)
        {
            return (IEnumerable<T>)GetTempObject(name) ?? new T[] { defVal };
        }

        public void AddObject(string name, object o)
        {
            variables[name] = o;
        }

        public object GetObject(string name)
        {
            object o;

            if (variables.TryGetValue(name, out o)) return o; else return null;
        }

        public void AddGameObject(string name, GameObject go)
        {
            variables[name] = go;
        }

        public GameObject GetGameObject(string name)
        {
            object o;

            if (variables.TryGetValue(name, out o) && o is GameObject) return o as GameObject; else return null;
        }

        public string GetString(string name)
        {
            return (string)GetObject(name);
        }

        public bool GetBool(string name)
        {
            return (bool)(GetObject(name) ?? false);
        }

        //public IEnumerable<T> GetEnumerable<T>(string name)
        //{
        //    return (IEnumerable<T>)GetObject(name) ?? new T[] { };
        //}

        //public IEnumerable<T> GetEnumerable<T>(string name, T defVal)
        //{
        //    return (IEnumerable<T>)GetObject(name) ?? new T[] { defVal };
        //}

        public ScriptObjectData AddScriptGameObject(string name, int instance, GameObject go, Vector3 pos, Vector3 intPos, bool grabbed = false,
            OpenStatus openStatus = OpenStatus.UNKNOWN)
        {
            var sod = new ScriptObjectData(go, pos, intPos, grabbed, openStatus);

            scriptObjects[Tuple.Create(name, instance)] = sod;
            return sod;
        }

        public ScriptObjectData AddScriptGameObject(ScriptObjectName name, GameObject go, Vector3 pos, Vector3 intPos, bool grabbed = false,
            OpenStatus openStatus = OpenStatus.UNKNOWN)
        {
            return AddScriptGameObject(name.Name, name.Instance, go, pos, intPos, grabbed, openStatus);
        }

        public GameObject GetScriptGameObject(ScriptObjectName name)
        {
            return GetScriptGameObject(name.Name, name.Instance);
        }

        public GameObject GetScriptGameObject(string name, int instance)
        {
            ScriptObjectData sod;

            if (GetScriptGameObjectData(name, instance, out sod)) return sod.GameObject;
            else return null;
        }

        public IEnumerable<GameObject> GetScriptGameObjects()
        {
            return scriptObjects.Values.Select(sod => sod.GameObject);
        }

        public bool GetScriptGameObjectData(ScriptObjectName name, out ScriptObjectData sod)
        {
            return GetScriptGameObjectData(name.Name, name.Instance, out sod);
        }

        public bool GetScriptGameObjectData(string name, int instance, out ScriptObjectData sod)
        {
            return scriptObjects.TryGetValue(Tuple.Create(name, instance), out sod);
        }

        public void RemoveObject(string name)
        {
            variables.Remove(name);
        }

        private IEnumerator DummyActionMethod(State s)
        {
            yield return null;
        }

        public IEnumerator<State> GetEnumerator()
        {
            yield return this;
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            yield return this;
        }

        public int Count { get { return 1; } }

        public State Last()
        {
            return this;
        }

    }

    public class StateList : IStateGroup
    {
        public static readonly StateList Empty = new StateList();

        private IList<IStateGroup> enumerators;
        int count;

        public int Count { get { return count; } }

        public StateList()
        {
            enumerators = new List<IStateGroup>();
            count = 0;
        }

        public StateList(params IStateGroup[] groups)
        {
            enumerators = new List<IStateGroup>(groups);
            count = enumerators.Sum(grp => grp.Count);
        }

        public StateList(StateList sl)
        {
            enumerators = new List<IStateGroup>(sl.enumerators);
            count = sl.count;
        }

        public State Last()
        {
            return enumerators.Last().Last();
        }

        public void Add(IStateGroup se)
        {
            enumerators.Add(se);
            count += se.Count;
        }

        IEnumerator<State> IEnumerable<State>.GetEnumerator()
        {
            foreach (IStateGroup se in enumerators)
            {
                foreach (State s in se)
                {
                    yield return s;
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            foreach (IStateGroup se in enumerators)
            {
                foreach (State s in se)
                {
                    yield return s;
                }
            }
        }
    }


    public class ScriptPair
    {
        public IAction Action { get; set; }
        public Func<IAction, State, IEnumerable<IStateGroup>> ProcessMethod { get; set; }  // (action, state) -> enumerator of next admissible states
    }

    public class PartialLocation
    {
        public Vector3 goal;
        public List<Vector3> path;
        public PartialLocation(Vector3 goal, List<Vector3> path)
        {
            this.goal = goal;
            this.path = path;
        }
    }

    // This class is used to speed up the computation of areas where it is worht walking. Maybe can be reduced
    public class InteractionCache
    {
        public Dictionary<KeyValuePair<InteractionType, int>, List<Vector3>> interaction_points;
        public Dictionary<KeyValuePair<InteractionType, int>, List<Vector3>> target_points;
        public Dictionary<int, PartialLocation> corner_dict;
        public Dictionary<int, float> current_dist;
        public InteractionCache()
        {
            interaction_points = new Dictionary<KeyValuePair<InteractionType, int>, List<Vector3>>();
            target_points = new Dictionary<KeyValuePair<InteractionType, int>, List<Vector3>>();
            corner_dict = new Dictionary<int, PartialLocation>();
            current_dist = new Dictionary<int, float>();
        }

        public bool RemainingPath(int char_index, Vector3 final_interaction, out Vector3 walk_pos, out Vector3? lookat_pos, float walk_distance = 1.0f)
        {
            PartialLocation kv;
            // if (false) 
            if (corner_dict.TryGetValue(char_index, out kv))
            {
                if (current_dist[char_index] != walk_distance)
                {

                    //if (kv.goal.Equals(final_interaction))
                    if (kv.goal.x == final_interaction.x && kv.goal.z == final_interaction.z)
                    {
                        NavMeshHit hit;
                        List<Vector3> new_corners = GameObjectUtils.GetWalkLocation(kv.path.ToArray(), out walk_pos, out lookat_pos, walk_distance);
                        if (lookat_pos == null)
                        {
                            corner_dict.Remove(char_index);
                            return false;
                        }

                        if (NavMesh.SamplePosition(walk_pos, out hit, 0.01f, NavMesh.AllAreas))
                        {
                            walk_pos = hit.position;
                            corner_dict[char_index] = new PartialLocation(kv.goal, new_corners);
                            Debug.Log($"Number of remaining corners: {new_corners.Count}");
                            Debug.Log($"CORNERS: {new_corners}");
                            return true;
                        }
                    }
                    corner_dict.Remove(char_index);
                }
            }
            walk_pos = new Vector3(0, 0, 0);
            lookat_pos = null;
            return false;
        }

        public bool HasInteractionPoints(InteractionType interaction, int key, out List<Vector3> ipl, out List<Vector3> target_point)
        {
            ipl = new List<Vector3>();
            target_point = ipl;
            if (interaction_points.TryGetValue(new KeyValuePair<InteractionType, int>(interaction, key), out ipl))
            {
                target_points.TryGetValue(new KeyValuePair<InteractionType, int>(interaction, key), out target_point);
                return true;
            }
            return false;
        }

        public void SetInteractionPoints(InteractionType interaction, int object_id, List<Vector3> ipl, List<Vector3> target_pos)
        {
            interaction_points[new KeyValuePair<InteractionType, int>(interaction, object_id)] = ipl;
            target_points[new KeyValuePair<InteractionType, int>(interaction, object_id)] = target_pos;
        }
    }

    public class ScriptExecutor
    {
        private List<GameObject> nameList;                          // Cache GameObjects in scene; map is path_name -> object
        private RoomSelector roomSelector;
        private IObjectSelectorProvider objectSelectorProvider;
        private IGameObjectPropertiesCalculator propCalculator;     // Class which can caclulate interaction positions
        public List<ScriptPair> script;                            // Script (filled with subsequent calls of AddAction)
        public List<ScriptLine> sLines;            // Original Script 
        private CharacterControl characterControl;                  // Class which can execute actions (Walk, Grab, etc.)
        private List<ICameraControl> cameraControls;                        // Camera control class
        private int gotoExecDepth;
        private System.Diagnostics.Stopwatch execStartTime;
        public ProcessingReport report;
        private bool randomizeExecution;                            // Randomize selection of interaction position
        private Recorder recorder;
        private int processingTimeLimit = 20 * 1000;                // Max search time for admissible solution (in milliseconds)
        private int charIndex;
        private bool find_solution;
        public bool smooth_walk;

        public InteractionCache interaction_cache;

        public static Hashtable actionsPerLine = new Hashtable();
        public static int currRunlineNo = 0; // The line no being executed now
        public static int currActionsFinished = 0; // The number of actions finished for currRunLineNo. Moving to the next line if currActionsFinished == actionsPerLine[currRunlineNo];
        

        // *****
        private TestDriver caller;


        public ScriptExecutor(IList<GameObject> nameList, RoomSelector roomSelector,
            IObjectSelectorProvider objectSelectorProvider, Recorder rcdr, int charIndex, InteractionCache interaction_cache, bool smooth_walk = false)

        {
            this.nameList = new List<GameObject>(nameList);
            this.roomSelector = roomSelector;
            this.objectSelectorProvider = objectSelectorProvider;
            this.charIndex = charIndex;
            this.find_solution = !(objectSelectorProvider is InstanceSelectorProvider);
            this.interaction_cache = interaction_cache;
            this.smooth_walk = smooth_walk;
            this.execStartTime = new System.Diagnostics.Stopwatch();

            propCalculator = new DefaultGameObjectPropertiesCalculator();
            script = new List<ScriptPair>();
            sLines = new List<ScriptLine>();
            recorder = rcdr;
            report = new ProcessingReport();
        }

        public bool SkipExecution { get; set; }
        public bool AutoDoorOpening { get; set; }

        public bool RandomizeExecution
        {
            get { return randomizeExecution; }
            set
            {
                randomizeExecution = value;
                if (randomizeExecution) RandomUtils.PermuteHead(nameList, nameList.Count);
            }
        }

        public int ProcessingTimeLimit { get { return processingTimeLimit / 1000; } set { processingTimeLimit = value * 1000; } }   // in seconds

        public bool Success { get { return report.Success; } }




        public void Initialize(CharacterControl chc, List<ICameraControl> cac)
        {
            characterControl = chc;
            chc.report = report;
            cameraControls = cac;
            script.Clear();
            sLines.Clear();
        }

        public void ClearScript()
        {
            script.Clear();
            sLines.Clear();
        }

        private IEnumerable<GameObject> SelectObjects(IObjectSelector selector)
        {
            foreach (var kv in nameList)
            {
                if (selector.IsSelectable(kv))
                    yield return kv;
            }
        }

        private IEnumerable<GameObject> SelectObjects(IObjectSelector selector, Func<GameObject, bool> filter)
        {
            foreach (GameObject kv in nameList)
            {
                if (selector.IsSelectable(kv) && filter(kv))
                    yield return kv;
            }
        }

        private IEnumerable<ObjectData> SelectObjects(ScriptObjectName name, IObjectSelector selector, State current)
        {
            ScriptObjectData sod;

            if (current.GetScriptGameObjectData(name, out sod))
            {
                yield return sod;
            }
            else
            {
                HashSet<GameObject> scriptGOs = new HashSet<GameObject>(current.GetScriptGameObjects());

                foreach (GameObject go in SelectObjects(selector))
                {
                    if (!scriptGOs.Contains(go))
                        yield return new ObjectData(go, go.transform.position);
                }
            }
        }

        #region AddAction methods for different actions

        public void AddAction(GotoAction a, bool find)
        {
            //Debug.Log(" I am at AddAction(GotoAction a, bool find) in ScriptExcution.cs");
            if (find)
                script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessFind((GotoAction)ac, s)) });
            else
                script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWalk((GotoAction)ac, s)) });
        }

        public void AddAction(GotowardsAction a, bool teleport)
        {
            if (teleport)
            {
                script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWalkTeleport((GotowardsAction)ac, s)) });
            }
            else
            {
                script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWalk((GotowardsAction)ac, s)) });
            }
        }

        public void AddAction(TurnAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessTurn((TurnAction)ac, s)) });
        }
        public void AddAction(GoforwardAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWalk((GoforwardAction)ac, s)) });
        }

        public void AddAction(WatchAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWatch((WatchAction)ac, s)) });
        }

        public void AddAction(SitAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSit((SitAction)ac, s)) });
        }

        public void AddAction(StandupAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessStandup((StandupAction)ac, s)) });
        }

        public void AddAction(SwitchOnAction a)
        {
            Debug.Log("I am at AddAction(SwitchOnAction a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSwitchOn((SwitchOnAction)ac, s)) });
        }

        public void AddAction(GrabAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessGrab((GrabAction)ac, s)) });
        }

        public void AddAction(DrinkAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessDrink((DrinkAction)ac, s)) });
        }

        public void AddAction(PhoneAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessPhone((PhoneAction)ac, s)) });
        }

        public void AddAction(TouchAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessTouch((TouchAction)ac, s)) });
        }

        public void AddAction(PutAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessPut((PutAction)ac, s)) });
        }

        public void AddAction(PutBackAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessPutBack((PutBackAction)ac, s)) });
        }

        public void AddAction(OpenAction a)
        {
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessOpen((OpenAction)ac, s)) });
        }

        public void AddAction(BrushAction a)
        {
            Debug.Log(" I am at AddActtion(BrushAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessBrush((BrushAction)ac, s)) });
        }

        public void AddAction(CutAction a)
        {
            Debug.Log(" I am at AddActtion(CutAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessCut((CutAction)ac, s)) });
        }

        public void AddAction(EatAction a)
        {
            Debug.Log(" I am at AddActtion(EatAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessEat((EatAction)ac, s)) });
        }

        public void AddAction(FoldAction a)
        {
            Debug.Log(" I am at AddActtion(FoldAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessFold((FoldAction)ac, s)) });
        }

        public void AddAction(JumpAction a)
        {
            Debug.Log(" I am at AddActtion(JumpAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessJump((JumpAction)ac, s)) });
        }

        public void AddAction(JumpUpAction a)
        {
            Debug.Log(" I am at AddActtion(JumpAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessJumpUp((JumpUpAction)ac, s)) });
        }

        public void AddAction(JumpDownAction a)
        {
            Debug.Log(" I am at AddActtion(JumpAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessJumpDown((JumpDownAction)ac, s)) });
        }

        public void AddAction(KneelAction a)
        {
            Debug.Log(" I am at AddActtion(KneelAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessKneel((KneelAction)ac, s)) });
        }

        public void AddAction(LiftAction a)
        {
            Debug.Log(" I am at AddActtion(LiftAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessLift((LiftAction)ac, s)) });
        }

        public void AddAction(DropAction a)
        {
            Debug.Log(" I am at AddActtion(DropAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessDrop((DropAction)ac, s)) });
        }

        public void AddAction(RinseAction a)
        {
            Debug.Log(" I am at AddActtion(RinseAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessRinse((RinseAction)ac, s)) });
        }

        public void AddAction(SquatAction a)
        {
            Debug.Log(" I am at AddActtion(SquatAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSquat((SquatAction)ac, s)) });
        }

        public void AddAction(SqueezeAction a)
        {
            Debug.Log(" I am at AddActtion(SqueezeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSqueeze((SqueezeAction)ac, s)) });
        }

        public void AddAction(StretchAction a)
        {
            Debug.Log(" I am at AddActtion(StretchAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessStretch((StretchAction)ac, s)) });
        }

        public void AddAction(SweepAction a)
        {
            Debug.Log(" I am at AddActtion(SweepAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSweep((SweepAction)ac, s)) });
        }

        public void AddAction(PutOnAction a)
        {
            //Debug.Log(" I am at AddActtion(JumoAxtion a) in ScriptExcution.cs");
            //script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessPutOn((PutOnAction)ac, s)) });
        }

        public void AddAction(PutOffAction a)
        {
            //Debug.Log(" I am at AddActtion(JumoAxtion a) in ScriptExcution.cs");
            //script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessPutOff((PutOffAction)ac, s)) });
        }

        public void AddAction(StirAction a)
        {
            Debug.Log(" I am at AddActtion(StirAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessStir((StirAction)ac, s)) });
        }

        public void AddAction(ThrowAction a)
        {
            Debug.Log(" I am at AddActtion(ThrowAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessThrow((ThrowAction)ac, s)) });
        }

        public void AddAction(TypeAction a)
        {
            Debug.Log(" I am at AddActtion(TypeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessType((TypeAction)ac, s)) });
        }

        public void AddAction(UnFoldAction a)
        {
            Debug.Log(" I am at AddActtion(UnfoldAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessUnFold((UnFoldAction)ac, s)) });
        }

        public void AddAction(VacuumAction a)
        {
            Debug.Log(" I am at AddActtion(VacuumeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessVacuum((VacuumAction)ac, s)) });
        }

        public void AddAction(WakeUpAction a)
        {
            Debug.Log(" I am at AddActtion(WakeupAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWakeUp((WakeUpAction)ac, s)) });
        }

        public void AddAction(WipeAction a)
        {
            Debug.Log(" I am at AddActtion(WipeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWipe((WipeAction)ac, s)) });
        }

        public void AddAction(WrapAction a)
        {
            Debug.Log(" I am at AddActtion(WrapAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWrap((WrapAction)ac, s)) });
        }

        public void AddAction(WriteAction a)
        {
            Debug.Log(" I am at AddActtion(WriteAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWrite((WriteAction)ac, s)) });
        }

        public void AddAction(FallAction a)
        {
            Debug.Log(" I am at AddActtion(FallAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessFall((FallAction)ac, s)) });
        }

        public void AddAction(FallSitAction a)
        {
            Debug.Log(" I am at AddActtion(FallSitAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessFallSit((FallSitAction)ac, s)) });
        }

        public void AddAction(FallTableAction a)
        {
            Debug.Log(" I am at AddActtion(FallTableAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessFallTable((FallTableAction)ac, s)) });
        }

        public void AddAction(FallBackAction a)
        {
            Debug.Log(" I am at AddActtion(FallBackAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessFallBack((FallBackAction)ac, s)) });
        }

        public void AddAction(StandAction a)    // no needed already impulement as stand up
        {
            //Debug.Log(" I am at AddActtion(WipeAxtion a) in ScriptExcution.cs");
            //script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessStand((StandAction)ac, s)) });
        }

        public void AddAction(StraddleAction a)
        {
            Debug.Log(" I am at AddActtion(StraddleAction a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessStraddle((StraddleAction)ac, s)) });
        }

        public void AddAction(LegOppAction a)
        {
            Debug.Log(" I am at AddActtion(WipeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessLegOpp((LegOppAction)ac, s)) });
        }

        /*
        public void AddAction(StandWithAction a)
        {
            Debug.Log(" I am at AddActtion(WipeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessStandWith((StandWithAction)ac, s)) });
        }

        public void AddAction(WalkWithAction a)
        {
            Debug.Log(" I am at AddActtion(WipeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessWalkWith((WalkWithAction)ac, s)) });
        }
        */
        public void AddAction(ScrubAction a)
        {
            Debug.Log(" I am at AddActtion(ScrubAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessScrub((ScrubAction)ac, s)) });
        }

        /*
        public void AddAction(SewAction a)
        {
            //Debug.Log(" I am at AddActtion(WipeAxtion a) in ScriptExcution.cs");
            //script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSew((SewAction)ac, s)) });
        }
        */
        public void AddAction(ShakeAction a)
        {
            Debug.Log(" I am at AddActtion(ShakeAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessShake((ShakeAction)ac, s)) });
        }

        public void AddAction(SmellAction a)
        {
            Debug.Log(" I am at AddActtion(SmellAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSmell((SmellAction)ac, s)) });
        }

        public void AddAction(SoakAction a)
        {
            Debug.Log(" I am at AddActtion(SoakAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessSoak((SoakAction)ac, s)) });
        }

        public void AddAction(PourAction a)
        {
            Debug.Log(" I am at AddActtion(PourAxtion a) in ScriptExcution.cs");
            script.Add(new ScriptPair() { Action = a, ProcessMethod = ((ac, s) => this.ProcessPour((PourAction)ac, s)) });
        }


        #endregion


        #region Process**** methods (create a state)

        private IEnumerable<IStateGroup> ProcessWalk(GotoAction a, State current)
        {
            //Debug.Log(" I am at ProcessWalk in ScriptExcution.cs");
            foreach (State s in ProcessWalkAction(a, current, 0.5f, float.MaxValue, false))
                yield return s;
            foreach (State s in ProcessWalkAction(a, current, 0.0f, 0.5f, true))
                yield return s;
        }

        private IEnumerable<IStateGroup> ProcessWalk(GotowardsAction a, State current)
        {
            if (a.Name.Name == "character")
            {
                // If closer to 2.5 m, no need to walk
                foreach (State s in ProcessWalkTowardsAction(a, current, 2.5f, float.MaxValue, true))
                    yield return s;
            }
            else
            {
                foreach (State s in ProcessWalkTowardsAction(a, current, 0.0f, float.MaxValue, true))
                    yield return s;
            }
        }

        private IEnumerable<IStateGroup> ProcessWalkTeleport(GotowardsAction a, State current)
        {
            //foreach (State s in ProcessWalkTowardsAction(a, current, 0.5f, float.MaxValue, false))
            //    yield return s;
            if (a.Name.Name == "character")
            {
                foreach (State s in ProcessWalkTowardsAction(a, current, 2.5f, float.MaxValue, true, true))
                    yield return s;
            }
            else
            {
                foreach (State s in ProcessWalkTowardsAction(a, current, 0.0f, float.MaxValue, true, true))
                    yield return s;
            }
        }

        private IEnumerable<IStateGroup> ProcessWalk(GoforwardAction a, State current)
        {
            foreach (State s in ProcessWalkForwardAction(a, current))
                yield return s;

        }

        private IEnumerable<IStateGroup> ProcessTurn(TurnAction a, State current)
        {
            foreach (State s in ProcessRotateAction(a, current))
                yield return s;

        }

        private IEnumerable<IStateGroup> ProcessFind(GotoAction a, State current)
        {
            foreach (State s in ProcessFindAction(a, current, false))
                yield return s;
            foreach (State s in ProcessWalkAction(a, current, 0.0f, float.MaxValue, true))
                yield return s;
        }

        // Process Walk action; new interaction position must be at least minIPDelta and at most
        // maxIPDelta from the currrent one
        private IEnumerable<IStateGroup> ProcessWalkAction(GotoAction a, State current, float minIPDelta, float maxIPDelta, bool addReportItem)
        {
            //Debug.Log(" I am at ProcessWalkAction in ScriptExcution.cs");
            bool canSelect = false;
            string errormessage = "Unknown";

            List<ObjectData> gods = SelectObjects(a.Name, a.Selector, current).ToList();
            foreach (ObjectData god in gods)
            {
                GameObject go = god.GameObject;
                ScriptObjectData opd;
                Vector3 goPos;
                Vector3? intPos;

                if (!current.GetScriptGameObjectData(a.Name, out opd))
                {
                    goPos = go.transform.position;
                    intPos = null;
                }
                else
                {
                    if (opd.Grabbed)
                        continue;
                    goPos = opd.Position;
                    intPos = opd.InteractionPosition;
                }


                if (this.find_solution)
                {
                    string allowedRoom = current.GetString("ROOM_CONSTRAINT");
                    string obj_room_name = go.RoomName();
                    if (allowedRoom != null && allowedRoom != roomSelector.ExtractRoomName(obj_room_name))
                        continue;
                }

                if (roomSelector.IsRoomName(a.Name.Name))
                {
                    if (!go.IsRoom())
                        continue;

                    Bounds roomBounds = go.transform.GetComponent<Properties_room>().bounds;
                    List<Vector3> ipl = GameObjectUtils.CalculateDestinationPositions(roomBounds.center, characterControl.gameObject, roomBounds);
                    goPos = ipl[0];

                    goPos.y = current.InteractionPosition.y;
                    NavMeshPath path = new NavMeshPath();
                    NavMesh.CalculatePath(current.InteractionPosition, goPos, NavMesh.AllAreas, path);
                    if (path.status == NavMeshPathStatus.PathComplete)
                    {
                        Debug.Log("Path complete");
                        canSelect = true;
                    }
                    else if (path.status == NavMeshPathStatus.PathInvalid)
                    {
                        Debug.Log("Path invalid");
                        errormessage = "Path invalid";
                        canSelect = false;
                    }
                    else if (path.status == NavMeshPathStatus.PathPartial)
                    {
                        Debug.Log("Path partial");
                        errormessage = "Path partially completed";
                        canSelect = false;
                    }


                    // IList<DoorAction> doors = characterControl.DoorControl.SelectDoorsOnPath(path.corners, false);
                    State s;
                    // if (doors.Count > 0)
                    // {
                    //     s = new State(current, a, doors[doors.Count - 1].posOne, ExecuteGoto);
                    //     s.AddScriptGameObject(a.Name, go, goPos, doors[doors.Count - 1].posOne);
                    // }
                    // else
                    // {
                    //     s = new State(current, a, goPos, ExecuteGoto);
                    // }

                    if (canSelect)
                    {
                        s = new State(current, a, goPos, ExecuteGoto);
                        s.AddActionFlag("GOTO_TURN");

                        if (this.find_solution)
                            s.AddObject("ROOM_CONSTRAINT", roomSelector.ExtractRoomName(go.name));


                        yield return s;
                    }

                }
                else if (a.Intention == InteractionType.SIT)
                {
                    var pc = go.GetComponent<Properties_chair>();

                    if (pc != null)
                    {
                        List<Properties_chair.SittableUnit> suList = pc.GetSittableUnits();
                        if (RandomizeExecution)
                            RandomUtils.PermuteHead(suList, suList.Count);

                        foreach (var su in suList)
                        {
                            Transform pi = su.GetTsfm_positionInteraction();

                            float ipDist = (current.InteractionPosition - new Vector3(pi.position.x, 0, pi.position.z)).magnitude;

                            if (ipDist < minIPDelta || ipDist > maxIPDelta)
                                continue;

                            GameObject lookAtGo = su.GetTsfm_lookAtBody().gameObject;
                            Transform target = su.tsfm_group;
                            Vector3 newIP = new Vector3(pi.position.x, 0, pi.position.z);
                            State s = new State(current, a, newIP, ExecuteGoto);

                            s.AddScriptGameObject(a.Name, go, goPos, newIP);
                            s.AddGameObject("GOTO_SIT_LOOK_AT_OBJECT", lookAtGo);
                            s.AddGameObject("GOTO_SIT_TARGET", target.gameObject);
                            s.RemoveObject("ROOM_CONSTRAINT");
                            if (current.GetString("CHARACTER_STATE") == "SITTING")
                            {
                                s.RemoveObject("CHARACTER_STATE");
                                s.AddActionFlag("STANDUP");
                            }
                            canSelect = true;
                            yield return s;
                        }
                    }
                }
                else if (a.Intention == InteractionType.CLOSE || a.Intention == InteractionType.OPEN)
                {
                    IList<Vector3> intPositions;

                    if (IsOpenable(Vector3.zero, opd, go, out intPositions, false, a.Intention))
                    {
                        List<Vector3> filteredIPs = new List<Vector3>();

                        List<int> filteredIdxes = new List<int>();
                        for (int i = 0; i < intPositions.Count; i++)
                        {
                            Vector3 newIP = intPositions[i];
                            float ipDist = (current.InteractionPosition - new Vector3(newIP.x, 0, newIP.z)).magnitude;

                            if (minIPDelta <= ipDist && ipDist <= maxIPDelta)
                            {
                                filteredIPs.Add(newIP);
                                filteredIdxes.Add(i);
                            }
                        }
                        if (filteredIPs.Count > 0)
                        {
                            State s = new State(current, a, filteredIPs[0], ExecuteGoto);

                            Properties_door pd = go.GetComponent<Properties_door>();
                            if (pd != null && a.Intention == InteractionType.OPEN)
                            {
                                Vector3[] lookAts = new Vector3[] { pd.lookAtPush, pd.lookAtPull };
                                List<Vector3> filteredLookAts = new List<Vector3>();
                                for (int i = 0; i < filteredIdxes.Count; i++)
                                {
                                    filteredLookAts.Add(lookAts[filteredIdxes[i]]);
                                }
                                s.AddTempObject("ALTERNATIVE_LOOK_AT", lookAts);
                            }

                            s.AddScriptGameObject(a.Name, go, goPos, filteredIPs[0]);
                            s.AddTempObject("ALTERNATIVE_IPS", filteredIPs);
                            s.RemoveObject("ROOM_CONSTRAINT");
                            s.AddActionFlag("GOTO_TURN");
                            if (current.GetString("CHARACTER_STATE") == "SITTING")
                            {
                                s.RemoveObject("CHARACTER_STATE");
                                s.AddActionFlag("STANDUP");
                            }
                            canSelect = true;

                            //DebugCalcPath(a.Name.Name, current.InteractionPosition, newIP);                        

                            yield return s;
                        }
                    }
                }
                else if (a.Intention == InteractionType.SWITCHON || a.Intention == InteractionType.SWITCHOFF)
                {
                    Vector3 switchPos;

                    if (IsSwitchable(Vector3.zero, go, out switchPos, false))
                    {
                        Vector3 newIP = switchPos + 0.75f * GetObjectFrontVec(go);
                        float ipDist = (current.InteractionPosition - new Vector3(newIP.x, 0, newIP.z)).magnitude;

                        if (ipDist < minIPDelta || ipDist > maxIPDelta)
                            continue;

                        State s = new State(current, a, newIP, ExecuteGoto);

                        s.AddScriptGameObject(a.Name, go, goPos, newIP);

                        if (this.find_solution)
                            s.RemoveObject("ROOM_CONSTRAINT");
                        s.AddActionFlag("GOTO_TURN");
                        if (current.GetString("CHARACTER_STATE") == "SITTING")
                        {
                            s.RemoveObject("CHARACTER_STATE");
                            s.AddActionFlag("STANDUP");
                        }
                        canSelect = true;
                        yield return s;
                    }
                }
                else
                {
                    Vector3 pos;
                    Vector3 tpos;
                    if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, minIPDelta))
                    {
                        //GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        //capsule.transform.position = pos;
                        //capsule.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
                        State s = new State(current, a, pos, ExecuteGoto);

                        s.AddScriptGameObject(a.Name, go, goPos, pos);
                        if (this.find_solution)
                            s.RemoveObject("ROOM_CONSTRAINT");
                        s.AddActionFlag("GOTO_TURN");
                        if (current.GetString("CHARACTER_STATE") == "SITTING")
                        {
                            s.RemoveObject("CHARACTER_STATE");
                            s.AddActionFlag("STANDUP");
                        }
                        canSelect = true;

                        //DebugCalcPath(a.Name.Name, current.InteractionPosition, pos)

                        yield return s;
                    }
                    else
                    {
                        if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, minIPDelta, 2.0f, null, null, true))
                        {
                            State s = new State(current, a, pos, ExecuteGoto);

                            s.AddScriptGameObject(a.Name, go, goPos, pos);
                            if (this.find_solution)
                                s.RemoveObject("ROOM_CONSTRAINT");
                            s.AddActionFlag("GOTO_TURN");
                            if (current.GetString("CHARACTER_STATE") == "SITTING")
                            {
                                s.RemoveObject("CHARACTER_STATE");
                                s.AddActionFlag("STANDUP");
                            }
                            canSelect = true;

                            //DebugCalcPath(a.Name.Name, current.InteractionPosition, pos)

                            yield return s;
                        }
                        errormessage = "No interaction positions";
                    }
                }
            }
            if (!canSelect && addReportItem)
            {
                report.AddItem("PROCESS WALK", $"Can not select object: {a.Name.Name}. REASON: {errormessage}");
            }
        }

        private IEnumerable<IStateGroup> ProcessRotateAction(TurnAction a, State current)
        {
            //Vector3 walk_pos = characterControl.gameObject.transform.position + characterControl.gameObject.transform.forward;
            State s = new State(current, a, Vector3.zero, ExecuteRotate);
            yield return s;
        }

        private IEnumerable<IStateGroup> ProcessWalkForwardAction(GoforwardAction a, State current)
        {
            Vector3 current_pos = characterControl.gameObject.transform.position;
            Vector3 current_dir = characterControl.gameObject.transform.forward;
            Vector3 walk_pos = current_pos + current_dir;
            RaycastHit hit;
            State s;
            if (Physics.Raycast(new Vector3(current_pos.x, (float)0.2, current_pos.z), current_dir, out hit) &&
                Math.Abs(hit.point.x - current_pos.x) < Math.Abs(current_dir.x))
            {
                walk_pos = Vector3.Lerp(current_pos, new Vector3(hit.point.x, 0, hit.point.z), (float)0.9);
            }

            s = new State(current, a, walk_pos, ExecuteGoforward);
            yield return s;
        }

        private IEnumerable<IStateGroup> ProcessWalkTowardsAction(GotowardsAction a, State current, float minIPDelta, float maxIPDelta, bool addReportItem, bool teleport = false)
        {

            float walk_dist = a.WalkDist;
            System.Diagnostics.Stopwatch processWalkStopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool canSelect = false;
            string errormessage = "Unknown";


            IEnumerable<ObjectData> gods = SelectObjects(a.Name, a.Selector, current);

            // Debug.Log($"number of candidate objects: {gods.Count}");           

            foreach (ObjectData god in gods)
            {

                GameObject go = god.GameObject;
                ScriptObjectData opd;
                Vector3 goPos;
                Vector3? intPos;

                if (!current.GetScriptGameObjectData(a.Name, out opd))
                {
                    goPos = go.transform.position;
                    intPos = null;
                }

                else
                {
                    if (TestDriver.dataProviders.ObjectPropertiesProvider.PropertiesForClass(a.Name.Name).Contains("GRABBABLE"))
                    {
                        goPos = go.transform.position;
                        intPos = null;
                    }
                    else
                    {
                        goPos = opd.Position;
                        intPos = opd.InteractionPosition;
                    }

                    //    if (opd.Grabbed) {

                    //        continue;
                    //    }

                    //}
                }



                if (roomSelector.IsRoomName(a.Name.Name))
                {


                    if (!go.IsRoom())
                        continue;

                    Bounds roomBounds = go.transform.GetComponent<Properties_room>().bounds;
                    List<Vector3> ipl;
                    List<Vector3> target_pos;
                    if (!interaction_cache.HasInteractionPoints(a.Intention, a.Name.Instance, out ipl, out target_pos))
                    {
                        ipl = GameObjectUtils.CalculateDestinationPositions(roomBounds.center, characterControl.gameObject, roomBounds);
                        target_pos = ipl;
                        interaction_cache.SetInteractionPoints(a.Intention, a.Name.Instance, ipl, target_pos);
                    }
                    goPos = ipl[0];

                    goPos.y = current.InteractionPosition.y;
                    bool path_is_ok = false;

                    Debug.Log($"GOAL POSITION: {goPos}");

                    Vector3? lookat_pos;
                    Vector3 walk_pos;
                    if (!teleport)
                    {
                        if (!interaction_cache.RemainingPath(charIndex, goPos, out walk_pos, out lookat_pos, walk_dist))
                        {
                            // Debug.Log("Cache not hit");

                            NavMeshPath path = new NavMeshPath();
                            NavMesh.CalculatePath(current.InteractionPosition, goPos, NavMesh.AllAreas, path);

                            // Debug.Log($"######## Current Location: {current.InteractionPosition}");
                            // Debug.Log($"######## First Corner: {path.corners[0]}");

                            if (path.status == NavMeshPathStatus.PathInvalid)
                            {
                                errormessage = "Path invalid";
                            }
                            else if (path.status == NavMeshPathStatus.PathPartial)
                            {
                                Debug.Log("Path partial");
                                errormessage = "Path partial";
                            }
                            else
                            {
                                Vector3[] corners = path.corners;
                                List<Vector3> new_corners = GameObjectUtils.GetWalkLocation(corners, out walk_pos, out lookat_pos, walk_dist);
                                interaction_cache.corner_dict[charIndex] = new PartialLocation(goPos, new_corners);
                                interaction_cache.current_dist[charIndex] = walk_dist;
                                path_is_ok = true;

                                //for (int pi = 0; pi < corners.Length; pi++)
                                //{
                                //    GameObject capsulegoal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                                //    capsulegoal.transform.position = (Vector3)corners[pi];
                                //    capsulegoal.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);


                                //}


                            }



                        }

                        else
                        {
                            // Debug.Log("Cache hit");
                            path_is_ok = true;

                        }
                    }
                    else
                    {
                        walk_pos = goPos;
                        path_is_ok = true;
                        lookat_pos = null;
                    }

                    if (path_is_ok == true)
                    {

                        State s = new State(current, a, walk_pos, ExecuteGotowards);

                        if (lookat_pos.HasValue)
                        {
                            s.AddObject("NEXT_LOOK_AT", (Vector3)lookat_pos);
                        }
                        else
                        {
                            s.AddObject("NEXT_LOOK_AT", goPos);
                        }

                        //s.AddScriptGameObject(a.Name, go, goPos, doors[doors.Count - 1].posOne);

                        //s = new State(current, a, doors[doors.Count - 1].posOne, ExecuteGotowards);
                        //s.AddScriptGameObject(a.Name, go, goPos, doors[doors.Count - 1].posOne);

                        // Debug capsule
                        //GameObject[] allObjects = GameObject.FindGameObjectsWithTag("Capsule");
                        //foreach (GameObject obj in allObjects)
                        //{
                        //    if (obj.transform.name == "Capsule")
                        //    {
                        //        GameObject.Destroy(obj);
                        //    }
                        //}
                        //GameObject capsulegoal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        //capsulegoal.transform.position = (Vector3)lookat_pos;
                        //capsulegoal.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                        //capsulegoal.GetComponent<MeshRenderer>().material.color = Color.red;




                        canSelect = true;

                        processWalkStopwatch.Stop();
                        Debug.Log(String.Format("processWalkTo time: {0}", processWalkStopwatch.ElapsedMilliseconds));

                        yield return s;
                    }


                }

                else
                {

                    Vector3 pos;
                    Vector3 tpos;
                    float? maxDistObject = null;
                    if (teleport)
                    {
                        // Make sure we are close enough
                        ObjectBounds bounds_object = ObjectBounds.FromGameObject(go);
                        ObjectBounds bounds_char = ObjectBounds.FromGameObject(characterControl.gameObject);
                        float b1 = Math.Max(bounds_char.bounds.extents.x, bounds_char.bounds.extents.z);
                        float b2 = Math.Max(bounds_object.bounds.extents.x, bounds_object.bounds.extents.z);
                        maxDistObject = Math.Max(b1, b2) + 1.3f;
                    }
                    if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, minIPDelta, null, maxDistObject))
                    {

                        //GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        //capsule.transform.position = pos;
                        //capsule.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

                        Vector3? lookat_pos = null;
                        Vector3 walk_pos;
                        Vector3 new_pos = new Vector3(pos.x, 0.0f, pos.z);
                        if (teleport)
                        {
                            walk_pos = new_pos;
                            lookat_pos = tpos;
                        }
                        else
                        {
                            NavMeshPath path = new NavMeshPath();


                            NavMesh.CalculatePath(current.InteractionPosition, new_pos, NavMesh.AllAreas, path);

                            //GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                            //capsule.transform.position = new_pos;
                            //capsule.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

                            //GameObject myLine = new GameObject();
                            //myLine.transform.position = new_pos;
                            //myLine.AddComponent<LineRenderer>();
                            //LineRenderer lr = myLine.GetComponent<LineRenderer>();
                            //lr.material = new Material(Shader.Find("Sprites/Default"));
                            //lr.positionCount = path.corners.Length;
                            //lr.startWidth = 0.25f;
                            //lr.endWidth = 0.25f;
                            //lr.startColor = Color.blue;
                            //lr.endColor = Color.red;
                            ////lr.SetPositions(path.corners);

                            //for (int pi = 0; pi < path.corners.Length; pi++)
                            //{
                            //    lr.SetPosition(pi, path.corners[pi]);

                            //}



                            //Debug.Log(path.corners[path.corners.Length - 1]);
                            List<Vector3> remain_path = GameObjectUtils.GetWalkLocation(path.corners, out walk_pos, out lookat_pos, walk_dist);
                            if (remain_path.Count == 1)
                            {
                                // If destination reached, look at the place
                                lookat_pos = tpos;
                            }
                            //else
                            //{
                            //    if (this.smooth_walk)
                            //{
                            //        lookat_pos = null;
                            //    }
                            //}
                            //GameObject capsulegoal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                            //capsulegoal.transform.position = (Vector3) lookat_pos;
                            //capsulegoal.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                        }

                        State s = new State(current, a, walk_pos, ExecuteGotowards);

                        if (a.Name.Name != "character")
                        {
                            s.AddScriptGameObject(a.Name, go, goPos, pos);

                        }
                        else
                        {
                            lookat_pos = goPos;
                        }
                        if (lookat_pos.HasValue)
                        {
                            s.AddObject("NEXT_LOOK_AT", (Vector3)lookat_pos);
                        }
                        else
                        {
                            s.AddActionFlag("GOTO_TURN");
                        }

                        canSelect = true;

                        //DebugCalcPath(a.Name.Name, current.InteractionPosition, pos)

                        yield return s;
                        break;
                    }
                    else
                    {
                        if (a.Name.Name == "character")
                        {
                            State s = new State(current, a, current.InteractionPosition, ExecuteGotowards);
                            canSelect = true;
                            s.AddObject("NEXT_LOOK_AT", (Vector3)goPos);
                            yield return s;
                        }
                        else
                        {
                            errormessage = "No interaction positions";
                        }
                    }
                }
            }
            if (!canSelect && addReportItem)
            {
                report.AddItem("PROCESS WALK", $"Can not select object: {a.Name.Name}. REASON: {errormessage}");
            }
        }

        private bool FindInteractionPoint(Vector3 interaction_pos, GameObject go, InteractionType interaction, out Vector3 ip, out Vector3 tpos,
                                          Vector3? intPos, float minIPDelta, float? maxIPDelta = null, float? maxDistObject = null, int? object_id = null, bool ignore_visiblity = false)
        {
            HandInteraction hi = go.GetComponent<HandInteraction>();
            List<Vector3> ipl = null;
            List<Vector3> tposl = null;

            ip = Vector3.zero;
            tpos = Vector3.zero;
            Vector3 front_vec = GetObjectFrontVec(go);

            if (!object_id.HasValue || !this.interaction_cache.HasInteractionPoints(interaction, (int)object_id, out ipl, out tposl))
            {
                if (hi != null && hi.switches != null)
                {
                    ipl = new List<Vector3>();
                    tposl = new List<Vector3>();
                    for (int i = 0; i < hi.switches.Count; i++)
                    {
                        if (interaction == InteractionType.UNSPECIFIED && hi.switches[i].action == HandInteraction.ActivationAction.Open)
                        {
                            Vector3 swp = hi.switches[i].switchPosition;

                            Vector3 pos = hi.switches[i].switchTransform.TransformPoint(swp);

                            float scalar_distance = 0.75f;
                            if (hi.switches[i].so.objState.open)
                            {
                                pos = hi.switches[i].originalPosition;
                                scalar_distance = 0.75f;
                            }
                            Vector3 mpos = pos + scalar_distance * front_vec;
                            ipl.Add(mpos);
                            tposl.Add(pos);
                        }
                        else
                        {
                            Vector3 pos = hi.switches[i].switchPosition;
                            if (hi.switches[i].switchTransform != null)
                                pos = hi.switches[i].switchTransform.TransformPoint(pos);
                            else
                                pos = go.transform.TransformPoint(pos);
                            Vector3 mpos = pos + 0.75f * front_vec;
                            ipl.Add(mpos);
                            tposl.Add(pos);
                        }

                    }

                }
                else
                {

                    ipl = intPos.HasValue ? new List<Vector3> { intPos.Value } : CalculateInteractionPositions(go, null, ignore_visiblity);
                    tposl = new List<Vector3>();

                    if (ipl.Count == 0)
                    {
                        return false;
                    }
                    for (int i = 0; i < ipl.Count; i++)
                    {
                        tposl.Add(go.transform.position);
                    }
                    if (randomizeExecution)
                        RandomUtils.PermuteHead(ipl, 7); // permute 7 closest positions
                }
                if (object_id.HasValue)
                    interaction_cache.SetInteractionPoints(interaction, (int)object_id, ipl, tposl);
            }
            int it = 0;
            foreach (Vector3 pos in ipl)
            {
                float ipDistance = (interaction_pos - new Vector3(pos.x, 0, pos.z)).magnitude;
                float objectDistance = 0.0f;

                if (maxDistObject.HasValue)
                {
                    Bounds center_obj = GameObjectUtils.GetBounds(go);
                    objectDistance = (new Vector3(center_obj.center.x, 0.0f, center_obj.center.z) - new Vector3(pos.x, 0, pos.z)).magnitude;

                }
                if (ipDistance >= minIPDelta)
                {
                    if (!maxIPDelta.HasValue || (maxIPDelta.HasValue && ipDistance <= maxIPDelta))
                    {

                        if (!maxDistObject.HasValue || maxDistObject.HasValue && objectDistance <= maxDistObject)
                        {
                            
                            tpos = tposl[it];
                            ip = pos;
                            return true;
                        }
                    }
                }
            }
            return false;
        }


        // Process find close action
        private IEnumerable<IStateGroup> ProcessFindAction(GotoAction a, State current, bool addReportItem)
        {
            bool canSelect = false;

            foreach (ObjectData god in SelectObjects(a.Name, a.Selector, current))
            {
                GameObject go = god.GameObject;
                ScriptObjectData sod;
                Vector3 goPos;
                Vector3? intPos;

                if (!current.GetScriptGameObjectData(a.Name, out sod))
                {
                    goPos = go.transform.position;
                    intPos = null;
                }
                else
                {
                    if (sod.Grabbed)
                        continue;
                    goPos = sod.Position;
                    intPos = sod.InteractionPosition;
                }

                if (a.Intention == InteractionType.SIT)
                {
                    var pc = go.GetComponent<Properties_chair>();

                    if (pc != null)
                    {
                        List<Properties_chair.SittableUnit> suList = pc.GetSittableUnits();

                        foreach (var su in suList)
                        {
                            Transform pi = su.GetTsfm_positionInteraction();

                            if ((current.InteractionPosition - new Vector3(pi.position.x, 0, pi.position.z)).magnitude < 0.3f)
                            {
                                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                                s.AddScriptGameObject(a.Name, go, goPos, current.InteractionPosition);
                                canSelect = true;
                                yield return s;
                            }
                        }
                    }
                }
                else if (a.Intention == InteractionType.CLOSE || a.Intention == InteractionType.OPEN)
                {
                    IList<Vector3> switchPositions;

                    if (IsOpenable(current.InteractionPosition, sod, go, out switchPositions, true, a.Intention))
                    {
                        State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                        s.AddScriptGameObject(a.Name, go, goPos, current.InteractionPosition);
                        canSelect = true;
                        yield return s;
                    }
                }
                else
                {
                    IInteractionArea area = propCalculator.GetInteractionArea(goPos, a.Intention);
                    if (area.ContainsPoint(new Vector2(current.InteractionPosition.x, current.InteractionPosition.z)))
                    {
                        State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                        s.AddScriptGameObject(a.Name, go, goPos, current.InteractionPosition);
                        canSelect = true;
                        yield return s;
                    }
                }
            }
            if (!canSelect && addReportItem)
            {
                report.AddItem("PROCESS FIND", $"Can not select object: {a.Name.Name}");
            }
        }

        private IEnumerable<IStateGroup> ProcessWatch(WatchAction a, State current)
        {
            Vector3 ip = new Vector3(current.InteractionPosition.x, 0.5f, current.InteractionPosition.z);

            if (current.GetString("CHARACTER_STATE") == "SITTING")
            {    // Cannot turn
                Vector3 sittingPos = current.GetGameObject("GOTO_SIT_TARGET").transform.position;
                Vector3 lookDir = current.GetGameObject("GOTO_SIT_LOOK_AT_OBJECT").transform.position - sittingPos;

                lookDir.y = 0.0f;

                ScriptObjectData sod;

                if (!current.GetScriptGameObjectData(a.Name, out sod))
                {
                    report.AddItem("PROCESS WATCH", $"Not found object: {a.Name}");
                }
                else
                {
                    GameObject go = sod.GameObject;

                    if (IsVisibleFromSegment(go, sittingPos, 1.0f, 1.8f, 0.2f, true))
                    {
                        Vector3 targetDir = go.transform.position - sittingPos;

                        targetDir.y = 0.0f;
                        if (Mathf.Abs(Vector3.Angle(targetDir, lookDir)) < 20 /* degrees */ &&
                                (sittingPos - go.transform.position).magnitude < 6.0f)
                        {
                            State s = new State(current, a, current.InteractionPosition, ExecuteWatch);
                            s.AddScriptGameObject(a.Name, go, sod.Position, current.InteractionPosition);
                            yield return s;
                        }
                    }
                    else
                    {
                        report.AddItem("PROCESS WATCH", $"Object {a.Name.Name} is not visible from sitting position");
                    }
                }
            }
            else
            { // Can turn
                bool canSelect = false;

                foreach (ObjectData god in SelectObjects(a.Name, a.Selector, current))
                {
                    GameObject go = god.GameObject;
                    IInteractionArea area = propCalculator.GetInteractionArea(god.Position, InteractionType.WATCH);

                    if (area.ContainsPoint(new Vector2(ip.x, ip.z)))
                    {
                        if (IsVisibleFromSegment(go, ip, 1.0f, 1.8f, 0.2f, true))
                        {
                            State s = new State(current, a, current.InteractionPosition, ExecuteTurn);
                            s.AddScriptGameObject(a.Name, go, god.Position, current.InteractionPosition);
                            canSelect = true;
                            yield return s;
                        }
                    }
                }
                if (!canSelect)
                {
                    report.AddItem("PROCESS WATCH", $"Can not select object to watch: {a.Name.Name}");
                }
            }
        }

        private IEnumerable<IStateGroup> ProcessSit(SitAction a, State current)
        {
            ScriptObjectData sod;
            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                Vector3 pos;

                List<ObjectData> gods = SelectObjects(a.Name, a.Selector, current).ToList();
                GameObject go = gods[0].GameObject;
                Vector3 goPos;
                Vector3? intPos;
                Vector3 tpos;

                if (go.GetComponent<Properties_chair>() == null)
                {
                    report.AddItem("PROCESS SIT", $"Object {a.Name} has no Properties_chair component");
                    return StateList.Empty;
                }

                goPos = go.transform.position;
                intPos = null;
                if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.SIT, out pos, out tpos, intPos, 0.0f, a.Name.Instance))
                {
                    sod = current.AddScriptGameObject(a.Name, go, goPos, pos);

                }
                else
                {
                    report.AddItem("PROCESS SIT", $"Not found object: {a.Name.Name}");
                    return StateList.Empty;
                }
            }
            if (IsInteractionPosition(current.InteractionPosition, sod, sod.Position, InteractionType.SIT, 0.5f))
            {
                return ProcessSitAction(a, current);
            }
            else
            {
                if (!this.smooth_walk)
                {
                    GotowardsAction ga = new GotowardsAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, InteractionType.SIT);
                    return CreateStateGroup(current, s => ProcessWalkTowardsAction(ga, s, 0, float.MaxValue, true, true), s => ProcessSitAction(a, s));
                }
                else
                {
                    GotoAction ga = new GotoAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, InteractionType.SIT);
                    return CreateStateGroup(current, s => ProcessWalkAction(ga, s, 0, float.MaxValue, true), s => ProcessSitAction(a, s));
                }

            }
        }

        private IEnumerable<IStateGroup> ProcessSitAction(SitAction a, State current)
        {
            ScriptObjectData sod;

            String characterState = (String)current.GetObject("CHARACTER_STATE");
            if (characterState != null && characterState.Equals("SITTING"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }


            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                report.AddItem("PROCESS SIT", $"Not found object: {a.Name}");
            }
            else
            {
                GameObject go = sod.GameObject;
                if (go.GetComponent<Properties_chair>() == null)
                {
                    report.AddItem("PROCESS SIT", $"Object {a.Name} has no Properties_chair component");
                    yield return StateList.Empty;
                }
                else
                {

                    GameObject lookAtGo = current.GetGameObject("GOTO_SIT_LOOK_AT_OBJECT");
                    GameObject target = current.GetGameObject("GOTO_SIT_TARGET");


                    if (lookAtGo == null || target == null)
                    {

                        //ScriptLine sl = sLines[a.ScriptLine];
                        //string name = sl.Parameters[0].Item1;
                        //int instance = sl.Parameters[0].Item2;
                        string name = a.Name.Name;
                        int instance = a.Name.Instance;
                        ScriptObjectName Name = new ScriptObjectName(name, instance);
                        foreach (ObjectData god in SelectObjects(Name, GetRoomOrObjectSelector(name, instance), current))
                        {

                            GameObject go1 = god.GameObject;
                            var pc = go1.GetComponent<Properties_chair>();
                            if (pc != null)
                            {
                                List<Properties_chair.SittableUnit> suList = pc.GetSittableUnits();
                                //if (RandomizeExecution)
                                RandomUtils.PermuteHead(suList, suList.Count);
                                //suList.Skip(3);
                                foreach (var su in suList)
                                {
                                    Transform pi = su.GetTsfm_positionInteraction();
                                    float ipDist = (current.InteractionPosition - new Vector3(pi.position.x, 0, pi.position.z)).magnitude;

                                    Vector3 newIP = new Vector3(pi.position.x, 0, pi.position.z);

                                    State s = new State(current, a, newIP, ExecuteSit);
                                    s.AddObject("CHARACTER_STATE", "SITTING");

                                    if (ipDist < 0.0f /*|| ipDist > 0.5f*/)
                                        continue;

                                    lookAtGo = su.GetTsfm_lookAtBody().gameObject;
                                    target = su.tsfm_group.gameObject;

                                    s.AddGameObject("GOTO_SIT_LOOK_AT_OBJECT", lookAtGo);
                                    s.AddGameObject("GOTO_SIT_TARGET", target);
                                    yield return s;
                                }
                            }

                        }

                    }
                    else
                    {
                        State s = new State(current, a, current.InteractionPosition, ExecuteSit);
                        s.AddGameObject("GOTO_SIT_LOOK_AT_OBJECT", lookAtGo);
                        s.AddGameObject("GOTO_SIT_TARGET", target);
                        s.AddObject("CHARACTER_STATE", "SITTING");
                        yield return s;
                    }
                }
            }
        }


        private IEnumerable<IStateGroup> ProcessStandup(StandupAction a, State current)
        {
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
        }

        private IEnumerable<IStateGroup> ProcessOpen(OpenAction a, State current)
        {
            ScriptObjectData sod;
            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                Vector3 pos;

                List<ObjectData> gods = SelectObjects(a.Name, a.Selector, current).ToList();
                GameObject go = gods[0].GameObject;
                Vector3 goPos;
                Vector3? intPos;
                Vector3 tpos;

                goPos = go.transform.position;
                intPos = null;
                if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, 0.0f, a.Name.Instance))
                {
                    sod = current.AddScriptGameObject(a.Name, go, goPos, pos);
                    //return ProcessOpenAction(a, current);
                }
                else
                {
                    report.AddItem("PROCESS OPEN", $"Not found object: {a.Name.Name}");
                    return StateList.Empty;
                }
            }

            if (!sod.Grabbed)
            {
                if (IsInteractionPosition(current.InteractionPosition, sod, sod.Position, a.Close ? InteractionType.CLOSE : InteractionType.OPEN, 0.5f))
                {
                    return ProcessOpenAction(a, current);
                }
                else
                {

                    if (!this.smooth_walk)
                    {
                        GotowardsAction ga = new GotowardsAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, a.Close ? InteractionType.CLOSE : InteractionType.OPEN);
                        return CreateStateGroup(current, s => ProcessWalkTowardsAction(ga, s, 0, float.MaxValue, true, true), s => ProcessOpenAction(a, s));
                    }
                    else
                    {
                        GotoAction ga = new GotoAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false,
                        a.Close ? InteractionType.CLOSE : InteractionType.OPEN);
                        return CreateStateGroup(current, s => ProcessWalkAction(ga, s, 0, float.MaxValue, true), s => ProcessOpenAction(a, s));
                    }
                }
            }

            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessOpenAction(OpenAction a, State current)
        {
            ScriptObjectData sod;

            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {

                report.AddItem("PROCESS OPEN", $"Not found object: {a.Name.Name}");
            }
            else
            {
                GameObject go = sod.GameObject;
                IList<Vector3> switchPositions;

                if (IsOpenable(current.InteractionPosition, sod, out switchPositions, true, a.Close ? InteractionType.CLOSE : InteractionType.OPEN))
                {
                    State s = new State(current, a, current.InteractionPosition, ExecuteOpen);

                    if (s.GetGameObject("RIGHT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                        s.AddScriptGameObject(a.Name, sod.GameObject, sod.Position, current.InteractionPosition,
                            sod.Grabbed, a.Close ? OpenStatus.CLOSED : OpenStatus.OPEN);
                        yield return s;
                    }
                    else if (s.GetGameObject("LEFT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                        s.AddScriptGameObject(a.Name, sod.GameObject, sod.Position, current.InteractionPosition,
                            sod.Grabbed, a.Close ? OpenStatus.CLOSED : OpenStatus.OPEN);
                        yield return s;
                    }
                }
            }
        }

        private IEnumerable<IStateGroup> ProcessGrab(GrabAction a, State current)
        {
            ScriptObjectData sod;
            if (!TestDriver.dataProviders.ObjectPropertiesProvider.PropertiesForClass(a.Name.Name).Contains("GRABBABLE"))
            {
                return StateList.Empty;

            }
            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                Vector3 pos;
                Vector3 tpos;
                List<ObjectData> gods = SelectObjects(a.Name, a.Selector, current).ToList();
                GameObject go = gods[0].GameObject;
                Vector3 goPos;
                Vector3? intPos;

                goPos = go.transform.position;
                intPos = null;

                if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, 0.0f))
                {
                    sod = current.AddScriptGameObject(a.Name, go, goPos, pos);
                    //return ProcessOpenAction(a, current);
                }
                else if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, 0.0f, 1.0f, null, null, true))
                {
                    sod = current.AddScriptGameObject(a.Name, go, goPos, pos);
                }
                else
                {
                    report.AddItem("PROCESS GRAB", $"Not found object: {a.Name.Name}");
                    return StateList.Empty;
                }
            }
            if (!sod.Grabbed)
            {
                if (IsInteractionPosition(current.InteractionPosition, sod, sod.Position, InteractionType.GRAB, 0))
                {
                    return ProcessGrabAction(a, current);
                }
                else
                {
                    //GotoAction ga = new GotoAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, InteractionType.GRAB);
                    //return CreateStateGroup(current, s => ProcessWalkAction(ga, s, 0, float.MaxValue, true), s => ProcessGrabAction(a, s));

                    if (!this.smooth_walk)
                    {
                        GotowardsAction ga = new GotowardsAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, InteractionType.GRAB);
                        return CreateStateGroup(current, s => ProcessWalkTowardsAction(ga, s, 0, float.MaxValue, true, true), s => ProcessGrabAction(a, s));
                    }
                    else
                    {
                        GotoAction ga = new GotoAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, InteractionType.GRAB);
                        return CreateStateGroup(current, s => ProcessWalkAction(ga, s, 0, float.MaxValue, true), s => ProcessGrabAction(a, s));
                    }
                }
            }

            return StateList.Empty;
        }

        private IEnumerable<IStateGroup> ProcessGrabAction(GrabAction a, State current)
        {
            ScriptObjectData sod;

            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                report.AddItem("PROCESS GRAB", $"Not found object: {a.Name.Name}");
            }
            else
            {
                // We can grab an object if:
                // - scripts are attached to it,
                // - no scripts but dimensions are ok (IsGrabbable)
                // TODO: check height of go, not only shape
                GameObject go = sod.GameObject;
                var hi = go.GetComponent<HandInteraction>();

                if ((hi == null && IsGrabbable(go)) || (hi != null && hi.allowPickUp))
                {
                    State s = new State(current, a, current.InteractionPosition, ExecuteGrab);
                    if (s.GetGameObject("RIGHT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                        s.AddGameObject("RIGHT_HAND_OBJECT", go);
                        s.AddScriptGameObject(a.Name, sod.GameObject, sod.Position, current.InteractionPosition, true);
                        yield return s;
                    }
                    else if (s.GetGameObject("LEFT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                        s.AddGameObject("LEFT_HAND_OBJECT", go);
                        s.AddScriptGameObject(a.Name, sod.GameObject, sod.Position, current.InteractionPosition, true);
                        yield return s;
                    }
                }
            }
        }

        // Grab (if necessary) + drink
        private IEnumerable<IStateGroup> ProcessDrink(DrinkAction a, State current)
        {
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS DRINK", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                Debug.Log("Drink left or right");
                return ProcessDrinkAction(a, current);
            }
            else
            {
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessDrinkAction(a, s));
            }
            return StateList.Empty;
        }

        private IEnumerable<IStateGroup> ProcessBrush(BrushAction a, State current)
        {

            Debug.Log("I am at ProcessBrush in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS BRUSH", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessBrushAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessBrush on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessBrushAction(a, s));
            }
            return StateList.Empty;
        }

        private IEnumerable<IStateGroup> ProcessCut(CutAction a, State current)
        {

            Debug.Log("I am at ProcessCut in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS CUT", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessCutAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessCut on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessCutAction(a, s));
            }
            return StateList.Empty;
        }

        private IEnumerable<IStateGroup> ProcessEat(EatAction a, State current)
        {

            Debug.Log("I am at ProcessEat in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS EAT", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessEatAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessEat on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessEatAction(a, s));
            }
            return StateList.Empty;
        }

        private IEnumerable<IStateGroup> ProcessDrop(DropAction a, State current)
        {

            Debug.Log("I am at ProcessDrop in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS DROP", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessDropAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessDrop on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessDropAction(a, s));
            }
            return StateList.Empty;
        }

        private IEnumerable<IStateGroup> ProcessRinse(RinseAction a, State current)
        {

            Debug.Log("I am at ProcessRinse in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS Rinse", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessRinseAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessRinse on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessRinseAction(a, s));
            }
            return StateList.Empty;
        }
         private IEnumerable<IStateGroup> ProcessSquat(SquatAction a, State current)
        {

            Debug.Log("ProcessSquat... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteSquat);
                yield return s;
            //}

        }
        private IEnumerable<IStateGroup> ProcessSqueeze(SqueezeAction a, State current)
        {

            Debug.Log("I am at ProcessSqueeze in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS SQUEEZE", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessSqueezeAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessSqueeze on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessSqueezeAction(a, s));
            }
            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessFold(FoldAction a, State current)
        {

            Debug.Log("ProcessFold... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteFold);
                yield return s;
            //}

        }
        private IEnumerable<IStateGroup> ProcessJump(JumpAction a, State current)
        {

            Debug.Log("ProcessJump... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteJump);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessJumpUp(JumpUpAction a, State current)
        {

            Debug.Log("ProcessJumpUp... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteJumpUp);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessJumpDown(JumpDownAction a, State current)
        {

            Debug.Log("ProcessJumpDown... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteJumpDown);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }
        private IEnumerable<IStateGroup> ProcessKneel(KneelAction a, State current)
        {

            Debug.Log("ProcessKneel... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteKneel);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }
        private IEnumerable<IStateGroup> ProcessLift(LiftAction a, State current)
        {

            Debug.Log("ProcessLift... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteLift);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

         private IEnumerable<IStateGroup> ProcessStretch(StretchAction a, State current)
        {

            Debug.Log("ProcessStretch... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteStretch);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }
         private IEnumerable<IStateGroup> ProcessSweep(SweepAction a, State current)
        {
            Debug.Log("I am at ProcessSweep in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS SWEEP", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessSweepAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessSweep on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessSweepAction(a, s));
            }
            return StateList.Empty;
        }
        
         private IEnumerable<IStateGroup> ProcessStir(StirAction a, State current)
        {
            Debug.Log("I am at ProcessStir in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS STIR", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessStirAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessStir on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessStirAction(a, s));
            }
            return StateList.Empty;
        }
        
        private IEnumerable<IStateGroup> ProcessThrow(ThrowAction a, State current)
        {

            Debug.Log("ProcessThrow... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteThrow);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessType(TypeAction a, State current)
        {

            Debug.Log("ProcessType... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteType);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessUnFold(UnFoldAction a, State current)
        {

            Debug.Log("ProcessUnFlod... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteUnFold);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }
        private IEnumerable<IStateGroup> ProcessVacuum(VacuumAction a, State current)
        {

            Debug.Log("ProcessVacuum... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteVacuum);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessWakeUp(WakeUpAction a, State current)
        {

            Debug.Log("ProcessWakeUp... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteWakeUp);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }
        private IEnumerable<IStateGroup> ProcessWipe(WipeAction a, State current)
        {
            Debug.Log("I am at ProcessWipe in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS WIPE", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessWipeAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessWipe on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessWipeAction(a, s));
            }
            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessWrap(WrapAction a, State current)
        {

            Debug.Log("I am at ProcessWrap in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS WRAP", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessWrapAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessWrap on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessWrapAction(a, s));
            }
            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessWrite(WriteAction a, State current)
        {

            Debug.Log("ProcessWrite... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteWrite);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessFall(FallAction a, State current)
        {

            Debug.Log("ProcessFall... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteFall);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessFallSit(FallSitAction a, State current)
        {

            Debug.Log("ProcessFallSit... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteFallSit);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessFallTable(FallTableAction a, State current)
        {

            Debug.Log("ProcessFallTable... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteFallTable);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessFallBack(FallBackAction a, State current)
        {

            Debug.Log("ProcessFallBack... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteFallBack);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessStraddle(StraddleAction a, State current)
        {

            Debug.Log("ProcessStraddle... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteStraddle);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessLegOpp(LegOppAction a, State current)
        {

            Debug.Log("ProcessLegOpp... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteLegOpp);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }

        private IEnumerable<IStateGroup> ProcessLegopp(LegOppAction a, State current)
        {

            Debug.Log("ProcessLegopp... CHARACTER_STATE = " + (string)current.GetObject("CHARACTER_STATE"));
            //if ((string)current.GetObject("CHARACTER_STATE") == null)
            //{
                State s = new State(current, a, current.InteractionPosition, ExecuteLegOpp);
                yield return s;
            //}

            /*
            if ((string)current.GetObject("CHARACTER_STATE") == "SITTING")
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStandup);
                s.RemoveObject("CHARACTER_STATE");
                s.RemoveObject("GOTO_SIT_LOOK_AT_OBJECT");
                s.RemoveObject("GOTO_SIT_TARGET");
                yield return s;
            }
            else
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteNone);
                yield return s;
            }
            */
        }
        private IEnumerable<IStateGroup> ProcessScrub(ScrubAction a, State current)
        {

            Debug.Log("I am at ProcessScrub in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS SCRUB", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessScrubAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessScrub on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessScrubAction(a, s));
            }
            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessShake(ShakeAction a, State current)
        {
            Debug.Log("I am at ProcessShake in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS SHAKE", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessShakeAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessShake on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessShakeAction(a, s));
            }
            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessSmell(SmellAction a, State current)
        {
            Debug.Log("I am at ProcessSmell in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS SMELL", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessSmellAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessSmell on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessSmellAction(a, s));
            }
            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessSoak(SoakAction a, State current)
        {
            Debug.Log("I am at ProcessSoak in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS POUR", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessSoakAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessShake on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessSoakAction(a, s));
            }
            return StateList.Empty;
        }
        private IEnumerable<IStateGroup> ProcessPour(PourAction a, State current)
        {
            Debug.Log("I am at ProcessPour in ScriptExcution.cs  " + a.Name);
            GameObject go = current.GetScriptGameObject(a.Name);
            Debug.Log("GameObject Name = " + go.name + "  in ScriptExcution.cs  ");
            if (go == null)
            {
                report.AddItem("PROCESS POUR", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessPourAction(a, current);
            }
            else
            {
                Debug.Log("I am at ProcessPour on target object in ScriptExcution.cs  ");
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessPourAction(a, s));
            }
            return StateList.Empty;
        }
        

        private IEnumerable<IStateGroup> ProcessBrushAction(BrushAction a, State current)
        {
            Debug.Log("I am at ProcessBrushAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS BRUSH", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteBrush);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteBrush);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessCutAction(CutAction a, State current)
        {
            Debug.Log("I am at ProcessCutAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS CUT", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteCut);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteCut);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessEatAction(EatAction a, State current)
        {
            Debug.Log("I am at ProcessEatAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS EAT", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteEat);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteEat);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessDropAction(DropAction a, State current)
        {
            Debug.Log("I am at ProcessDropAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS DROP", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteDrop);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteDrop);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessRinseAction(RinseAction a, State current)
        {
            Debug.Log("I am at ProcessRinseAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS RINSE", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteRinse);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteRinse);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessSqueezeAction(SqueezeAction a, State current)
        {
            Debug.Log("I am at ProcessSqueezeAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS SQUEEZE", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSqueeze);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSqueeze);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }
         private IEnumerable<IStateGroup> ProcessSweepAction(SweepAction a, State current)
        {
            Debug.Log("I am at ProcessSweepAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS SWEEP", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSweep);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSweep);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessStirAction(StirAction a, State current)
        {
            Debug.Log("I am at ProcessStirAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS STIR", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStir);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteStir);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }
        private IEnumerable<IStateGroup> ProcessWipeAction(WipeAction a, State current)
        {
            Debug.Log("I am at ProcessWipeAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS WIPE", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteWipe);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteWipe);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }
        private IEnumerable<IStateGroup> ProcessWrapAction(WrapAction a, State current)
        {
            Debug.Log("I am at ProcessWrapAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS WIPE", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteWrap);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteWrap);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }
        private IEnumerable<IStateGroup> ProcessScrubAction(ScrubAction a, State current)
        {
            Debug.Log("I am at ProcessScrubAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS SCRUB", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteScrub);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteScrub);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessShakeAction(ShakeAction a, State current)
        {
            Debug.Log("I am at ProcessShakeAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS SHAKE", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteShake);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteShake);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }

        private IEnumerable<IStateGroup> ProcessSmellAction(SmellAction a, State current)
        {
            Debug.Log("I am at ProcessSmellAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS POUR", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSmell);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSmell);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }
           private IEnumerable<IStateGroup> ProcessSoakAction(SoakAction a, State current)
        {
            Debug.Log("I am at ProcessSoakAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS POUR", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSoak);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteSoak);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }
        private IEnumerable<IStateGroup> ProcessPourAction(PourAction a, State current)
        {
            Debug.Log("I am at ProcessPourAction in ScriptExcution.cs");
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS POUR", $"Not found object: {a.Name.Name}");
            }
            
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecutePour);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecutePour);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
          
        }


        // Grab (if necessary) + phone action (talk, text)


        private IEnumerable<IStateGroup> ProcessPhone(PhoneAction a, State current)
        {
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS PHONE", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT") || go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                return ProcessPhoneAction(a, current);
            }
            else
            {
                GrabAction ga = new GrabAction(a.ScriptLine, a.Selector, a.Name.Name, a.Name.Instance);
                return CreateStateGroup(current, s => ProcessGrab(ga, s), s => ProcessPhoneAction(a, s));
            }
            return StateList.Empty;
        }

        private IEnumerable<IStateGroup> CreateStateGroup(State s0, Func<State, IEnumerable<IStateGroup>> procF1,
            Func<State, IEnumerable<IStateGroup>> procF2)
        {
            foreach (IStateGroup sg1 in procF1(s0))
            {
                State s1 = sg1.Last();
                foreach (IStateGroup s2 in procF2(s1))
                {
                    yield return new StateList(s1, s2);
                }
            }
        }

        private IEnumerable<IStateGroup> ProcessDrinkAction(DrinkAction a, State current)
        {
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS DRINK", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteDrink);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecuteDrink);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
        }

        private IEnumerable<IStateGroup> ProcessPhoneAction(PhoneAction a, State current)
        {
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddItem("PROCESS PHONE ACTION", $"Not found object: {a.Name.Name}");
            }
            else if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecutePhoneAction);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                yield return s;
            }
            else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
            {
                State s = new State(current, a, current.InteractionPosition, ExecutePhoneAction);
                s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                yield return s;
            }
        }

        private IEnumerable<IStateGroup> ProcessPut(PutAction a, State current)
        {
            ScriptObjectData sod;
            ScriptObjectData sodDest;

            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                report.AddItem("PROCESS PUT", $"Not found source object: {a.Name.Name}");
                return StateList.Empty;
            }

            if (!current.GetScriptGameObjectData(a.DestName, out sodDest))
            {
                Vector3 pos;
                Vector3 tpos;
                List<ObjectData> gods = SelectObjects(a.DestName, a.Selector, current).ToList();

                GameObject go = gods[0].GameObject;
                Vector3 goPos;
                Vector3? intPos;

                goPos = go.transform.position;
                intPos = null;
                if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, 0.0f))
                {
                    sodDest = current.AddScriptGameObject(a.DestName, go, goPos, pos);
                    //return ProcessOpenAction(a, current);
                }
                else
                {
                    report.AddItem("PROCESS PUT", $"Not found object: {a.DestName.Name}");
                    return StateList.Empty;
                }

            }

            if (sod.Grabbed)
            {
                if (IsInteractionPosition(current.InteractionPosition, sodDest, sodDest.Position, InteractionType.PUT, float.MaxValue))
                {
                    return ProcessPutAction(a, current);
                }
                else
                {
                    // When timescale is fast, better to just teleport
                    if (!this.smooth_walk)
                    {
                        GotowardsAction ga = new GotowardsAction(a.ScriptLine, EmptySelector.Instance, a.DestName.Name, a.DestName.Instance, false, InteractionType.PUT);
                        return CreateStateGroup(current, s => ProcessWalkTowardsAction(ga, s, 0, float.MaxValue, true, true), s => ProcessPutAction(a, s));
                    }
                    else
                    {
                        GotoAction ga = new GotoAction(a.ScriptLine, EmptySelector.Instance, a.DestName.Name, a.DestName.Instance, false, InteractionType.PUT);
                        return CreateStateGroup(current, s => ProcessWalkAction(ga, s, 0, float.MaxValue, true), s => ProcessPutAction(a, s));
                    }
                }
            }

            return StateList.Empty;
        }


        private IEnumerable<IStateGroup> ProcessPutAction(PutAction a, State current)
        {
            ScriptObjectData sod;
            ScriptObjectData sodDest;

            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                report.AddItem("PROCESS PUT", $"Not found source object: {a.Name.Name}");
            }
            else if (!current.GetScriptGameObjectData(a.DestName, out sodDest))
            {
                report.AddItem("PROCESS PUT", $"Not found destination object: {a.DestName.Name}");
            }
            else
            {
                FullBodyBipedEffector? fbbe = null;

                if (sod.GameObject == current.GetGameObject("RIGHT_HAND_OBJECT"))
                    fbbe = FullBodyBipedEffector.RightHand;
                else if (sod.GameObject == current.GetGameObject("LEFT_HAND_OBJECT"))
                    fbbe = FullBodyBipedEffector.LeftHand;

                if (fbbe != null)
                {
                    // We need to calculate putback position
                    foreach (Vector3 pos in GameObjectUtils.CalculatePutPositions(current.InteractionPosition, sod.GameObject, sodDest.GameObject, a.PutInside, false))
                    {
                        State s = new State(current, a, current.InteractionPosition, ExecutePut);
                        s.AddScriptGameObject(a.Name, sod.GameObject, pos, current.InteractionPosition, false);
                        s.AddObject("PUT_POSITION", pos);
                        s.AddObject("INTERACTION_HAND", fbbe);
                        if (fbbe == FullBodyBipedEffector.RightHand)
                            s.RemoveObject("RIGHT_HAND_OBJECT");
                        else if (fbbe == FullBodyBipedEffector.LeftHand)
                            s.RemoveObject("LEFT_HAND_OBJECT");
                        yield return s;
                    }
                }
            }
        }

        private bool IsInteractionPosition(Vector3 pos, ScriptObjectData sod, Vector3 goPos, InteractionType intention, float maxIPDelta)
        {
            GameObject go = sod.GameObject;

            if (intention == InteractionType.SIT)
            {
                var pc = go.GetComponent<Properties_chair>();

                if (pc == null)
                    return false;

                List<Properties_chair.SittableUnit> suList = pc.GetSittableUnits();

                foreach (var su in suList)
                {
                    Transform pi = su.GetTsfm_positionInteraction();
                    float ipDist = (pos - new Vector3(pi.position.x, 0, pi.position.z)).magnitude;

                    if (ipDist <= maxIPDelta)
                    {
                        // check if object is visible from interaction position
                        if (IsVisible(go, new Vector3(pi.position.x, 0.5f, pi.position.z)))
                            return true;
                    }
                }
            }
            else if (intention == InteractionType.CLOSE || intention == InteractionType.OPEN)
            {
                IList<Vector3> intPositions;

                if (IsOpenable(Vector3.zero, sod, out intPositions, false, intention))
                {
                    foreach (Vector3 newIP in intPositions)
                    {
                        //Vector3 newIP = switchPos + 0.75f * go.transform.right;
                        float ipDist = (pos - newIP).magnitude;

                        return ipDist <= maxIPDelta;
                    }
                }
            }
            else if (intention == InteractionType.SWITCHON || intention == InteractionType.SWITCHOFF)
            {
                Vector3 switchPos;

                if (IsSwitchable(Vector3.zero, go, out switchPos, false))
                {
                    float ipDist = (pos - switchPos).magnitude;

                    return ipDist <= maxIPDelta;
                }
            }
            else
            {
                IInteractionArea area = propCalculator.GetInteractionArea(goPos, intention);

                return area.ContainsPoint(new Vector2(pos.x, pos.z));
            }
            return false;
        }

        private IEnumerable<IStateGroup> ProcessPutBack(PutBackAction a, State current)
        {
            ScriptObjectData sod;

            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                report.AddItem("PROCESS PUTBACK", $"Not found object: {a.Name.Name}");
            }
            else if (!sod.Grabbed)
            {
                report.AddItem("PROCESS PUTBACK", $"Object {a.Name.Name} not grabbed");
            }
            else
            {
                FullBodyBipedEffector? fbbe = null;
                GameObject go = sod.GameObject;

                if (go == current.GetGameObject("RIGHT_HAND_OBJECT"))
                    fbbe = FullBodyBipedEffector.RightHand;
                else if (go == current.GetGameObject("LEFT_HAND_OBJECT"))
                    fbbe = FullBodyBipedEffector.LeftHand;

                if (fbbe != null)
                {
                    State s = new State(current, a, sod.InteractionPosition, ExecutePutBack);

                    s.AddObject("PUT_POSITION", sod.Position);
                    s.AddObject("INTERACTION_HAND", fbbe);
                    s.AddScriptGameObject(a.Name, go, sod.Position, sod.InteractionPosition, false);
                    if (this.find_solution)
                        s.RemoveObject("ROOM_CONSTRAINT");
                    if (current.GetString("CHARACTER_STATE") == "SITTING")
                    {
                        s.RemoveObject("CHARACTER_STATE");
                        s.AddActionFlag("STANDUP");
                    }
                    yield return s;
                }
            }

        }

        private IEnumerable<State> ProcessTouch(TouchAction a, State current)
        {
            GameObject go = current.GetScriptGameObject(a.Name);

            if (go == null)
            {
                report.AddProcessingItem("PROCESS TOUCH", $"Not found object: {a.Name.Name}");
            }
            else
            {
                Vector3 touchPos;
                //Vector3? goPos;
                //Vector3? intPos;

                //current.GetObjectAndInteractionPosition(go, out goPos, out intPos);
                //if (intPos.HasValue && IsTouchable(intPos.Value, go, out touchPos)) {
                if (IsTouchable(current.InteractionPosition, go, out touchPos))
                {
                    State s = new State(current, a, current.InteractionPosition, ExecuteTouch);
                    if (s.GetGameObject("RIGHT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                        s.AddObject("TOUCH_POSITION", touchPos);
                        yield return s;
                    }
                    else if (s.GetGameObject("LEFT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                        s.AddObject("TOUCH_POSITION", touchPos);
                        yield return s;
                    }
                }
            }
        }

        private IEnumerable<IStateGroup> ProcessSwitchOn(SwitchOnAction a, State current)
        {

            Debug.Log("I am at ProcessSwitchOn in ScriptExcution.cs");
            ScriptObjectData sod;
            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {
                Vector3 pos;

                List<ObjectData> gods = SelectObjects(a.Name, a.Selector, current).ToList();
                GameObject go = gods[0].GameObject;
                Debug.Log(" gameobject name = " + go.name);
                Vector3 goPos;
                Vector3? intPos;
                Vector3 tpos;

                goPos = go.transform.position;
                intPos = null;
                if (FindInteractionPoint(current.InteractionPosition, go, InteractionType.UNSPECIFIED, out pos, out tpos, intPos, 0.0f, a.Name.Instance))
                {
                    sod = current.AddScriptGameObject(a.Name, go, goPos, pos);
                    //return ProcessOpenAction(a, current);
                }
                else
                {
                    report.AddItem("PROCESS SWITCH ON", $"Not found object: {a.Name.Name}");
                    return StateList.Empty;
                }
            }

            if (!sod.Grabbed)
            {
                if (IsInteractionPosition(current.InteractionPosition, sod, sod.Position, InteractionType.SWITCHON, 0.5f))
                {
                    return ProcessSwitchOnAction(a, current);
                }
                else
                {

                    if (!this.smooth_walk)
                    {
                        GotowardsAction ga = new GotowardsAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, InteractionType.SWITCHON);
                        return CreateStateGroup(current, s => ProcessWalkTowardsAction(ga, s, 0, float.MaxValue, true, true), s => ProcessSwitchOnAction(a, s));
                    }
                    else
                    {
                        GotoAction ga = new GotoAction(a.ScriptLine, EmptySelector.Instance, a.Name.Name, a.Name.Instance, false, InteractionType.SWITCHON);
                        return CreateStateGroup(current, s => ProcessWalkAction(ga, s, 0, float.MaxValue, true), s => ProcessSwitchOnAction(a, s));
                    }
                }
            }

            return StateList.Empty;
        }

        private IEnumerable<State> ProcessSwitchOnAction(SwitchOnAction a, State current)
        {
            ScriptObjectData sod;


            if (!current.GetScriptGameObjectData(a.Name, out sod))
            {

                report.AddItem("PROCESS SWITCH ON", $"Not found object: {a.Name.Name}");
            }
            else
            {
                GameObject go = sod.GameObject;
                var hi = go.GetComponent<HandInteraction>();

                if (hi != null && hi.SwitchIndex(HandInteraction.ActivationAction.SwitchOn) >= 0)
                {
                    State s = new State(current, a, current.InteractionPosition, ExecuteSwitchOnPrefab);
                    if (s.GetGameObject("RIGHT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.RightHand);
                        yield return s;
                    }
                    else if (s.GetGameObject("LEFT_HAND_OBJECT") == null)
                    {
                        s.AddObject("INTERACTION_HAND", FullBodyBipedEffector.LeftHand);
                        yield return s;
                    }
                }
            }

        }

        #endregion

        #region Execute**** methods

        private IEnumerator ExecuteNone(State s)
        {
            yield return null;
        }

        private IEnumerator ExecuteWatch(State s)
        {
            WatchAction wa = (WatchAction)s.Action;

            recorder.MarkActionStart(InteractionType.WATCH, wa.ScriptLine);
            yield return new WaitForSeconds(wa.Duration);
        }

        private IEnumerator ExecuteGoforward(State s)
        {

            yield return ExecuteWalkOrRunTowards(s, false, null, null);
        }

        private IEnumerator ExecuteGotowards(State s)
        {
            GotowardsAction ga = (GotowardsAction)s.Action;

            if (s.HasActionFlag("GOTO_TURN"))
                yield return ExecuteWalkOrRunTowards(s, ga.Run, ga.Name, null);
            else if (s.GetObject("NEXT_LOOK_AT") != null)
                yield return ExecuteWalkOrRunTowards(s, ga.Run, null, (Vector3)s.GetObject("NEXT_LOOK_AT"));
            else
                yield return ExecuteWalkOrRunTowards(s, ga.Run, null, null);
        }

        private IEnumerator ExecuteGoto(State s)
        {
            GotoAction ga = (GotoAction)s.Action;

            if (s.HasActionFlag("GOTO_TURN"))
                yield return ExecuteWalkOrRun(s, ga.Run, ga.Name);
            else
                yield return ExecuteWalkOrRun(s, ga.Run, null);
        }

        private IEnumerator ExecuteWalkOrRun(State s, bool run, ScriptObjectName? turnToObjectName)
        {
            //Debug.Log("I am at ExecuteWalkOrRun in ScriptExcution.cs");
            if (s.HasActionFlag("STANDUP"))
                yield return ExecuteStandup(s);
            recorder.MarkActionStart(run ? InteractionType.RUN : InteractionType.WALK, s.Action.ScriptLine);

            IEnumerable<Vector3> lookAt = s.GetTempEnumerable<Vector3>("ALTERNATIVE_LOOK_AT");

            if (lookAt.Count() == 0)
            {
                List<Vector3> temp = new List<Vector3>();
                if (turnToObjectName.HasValue)
                {
                    GameObject go = s.GetScriptGameObject(turnToObjectName.Value);
                    if (go != null)
                    {
                        temp.Add(go.transform.position);
                    }
                }
                lookAt = temp;
            }
            if (AutoDoorOpening)
            {
                yield return characterControl.StartCoroutine(characterControl.walkOrRunToWithDoorOpening(recorder,
                    s.Action.ScriptLine, !run, s.GetTempEnumerable("ALTERNATIVE_IPS", s.InteractionPosition), lookAt));
            }
            else
            {

                //GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                //capsule.transform.position = s.InteractionPosition;
                //capsule.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);

                yield return characterControl.StartCoroutine(characterControl.walkOrRunTo(!run,
                    s.GetTempEnumerable("ALTERNATIVE_IPS", s.InteractionPosition), lookAt));
            }
        }

        private IEnumerator ExecuteWalkOrRunTowards(State s, bool run, ScriptObjectName? turnToObjectName, Vector3? next_look)
        {
            Debug.Log("I am at ExecuteWalkOrRunTowards in ScriptExcution.cs");
            recorder.MarkActionStart(run ? InteractionType.RUN : InteractionType.WALK, s.Action.ScriptLine);

            if (this.smooth_walk)
            {
                //next_look = null;
                NavMeshAgent nma = characterControl.GetComponent<NavMeshAgent>();
                //nma.autoBraking = true;
                nma.autoRepath = true;
                nma.stoppingDistance = 0.3f;


                yield return characterControl.StartCoroutine(characterControl.walkOrRunTo(!run, s.InteractionPosition, next_look));
            }
            else
            {
                yield return characterControl.StartCoroutine(characterControl.walkOrRunTeleport(!run,
                s.InteractionPosition, next_look, false));
            }


        }

        private IEnumerator ExecuteRotate(State s)
        {
            recorder.MarkActionStart(InteractionType.TURNLEFT, s.Action.ScriptLine);
            float degrees = ((TurnAction)s.Action).Degrees;
            yield return characterControl.StartCoroutine(characterControl.TurnDegrees(degrees));
        }

        private IEnumerator ExecuteTurn(State s)
        {
            recorder.MarkActionStart(InteractionType.TURNTO, s.Action.ScriptLine);
            WatchAction wa = (WatchAction)s.Action;
            yield return characterControl.StartCoroutine(characterControl.Turn(s.GetScriptGameObject(wa.Name).transform.position));
        }

        private IEnumerator ExecuteSit(State s)
        {
            recorder.MarkActionStart(InteractionType.SIT, s.Action.ScriptLine);
            SitAction wa = (SitAction)s.Action;


            GameObject lookAtGo = s.GetGameObject("GOTO_SIT_LOOK_AT_OBJECT");
            GameObject target = s.GetGameObject("GOTO_SIT_TARGET");
            bool perform_animation = true;
            if (this.smooth_walk)
            {
                perform_animation = false;
            }
            yield return characterControl.StartCoroutine(characterControl.Sit(s.GetScriptGameObject(wa.Name),
                lookAtGo, target, perform_animation));
        }

        private IEnumerator ExecuteStandup(State s)
        {
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.STANDUP, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Stand());
        }

        private IEnumerator ExecuteOpen(State s)
        {
            OpenAction ga = (OpenAction)s.Action;
            GameObject go = s.GetScriptGameObject(ga.Name);
            Bounds goBounds = GameObjectUtils.GetBounds(go);
            HandInteraction hi = go.GetComponent<HandInteraction>();
            Properties_door pd = go.GetComponent<Properties_door>();

            if (s.InteractionPosition != s.Previous.InteractionPosition)
                yield return ExecuteWalkOrRun(s, false, ga.Name);

            recorder.MarkActionStart(ga.Close ? InteractionType.CLOSE : InteractionType.OPEN, ga.ScriptLine);
            //
            goBounds.Encapsulate(GameObjectUtils.GetBounds(characterControl.gameObject));

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetFocusArea(goBounds);
            }

            if (pd == null)
            {
                // Fully open the cabinet
                if (go.name.ToLower().Contains("cabinet"))
                {
                    List<int> swis = hi.SwitchIndices(HandInteraction.ActivationAction.Open);
                    foreach (int swi in swis)
                    {
                        yield return characterControl.StartInteraction(go, (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND"),
                        swi);
                        hi.switches[swi].UpdateStateObject();
                    }
                }
                else
                {
                    int swi = hi.SwitchIndex(HandInteraction.ActivationAction.Open);

                    yield return characterControl.StartInteraction(go, (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND"),
                    swi);
                    hi.switches[swi].UpdateStateObject();
                }
            }
            else
            {
                if (ga.Close)
                {
                    int swi = hi.SwitchIndex(HandInteraction.ActivationAction.Open);
                    yield return characterControl.StartInteraction(go, (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND"),
                        swi);

                    if (AutoDoorOpening)
                        characterControl.DoorControl.MarkClosed(pd);
                }
                else
                {
                    yield return characterControl.DoorOpenLeft(go);
                    if (AutoDoorOpening)
                        characterControl.DoorControl.MarkOpen(pd);
                }
            }
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearFocusArea();
            }
        }

        private IEnumerator ExecuteGrab(State s)
        {
            GrabAction ga = (GrabAction)s.Action;
            GameObject go = s.GetScriptGameObject(ga.Name);

            if (go.GetComponent<HandInteraction>() == null)
            {
                var hi = go.AddComponent<HandInteraction>();
                hi.added_runtime = true;
                hi.allowPickUp = true;
                hi.grabHandPose = GetGrabPose(go).Value; // HandInteraction.HandPose.GrabVertical;
                hi.Initialize();
            }
            recorder.MarkActionStart(InteractionType.GRAB, ga.ScriptLine);
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetFocusObject(go);
            }
            //if (smooth_walk){
            yield return characterControl.GrabObject(go, (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND"));

            //}
            ClearFocusObject();
        }

        private IEnumerator ExecuteDrink(State s)
        {
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.DRINK, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.DrinkLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.DrinkRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecutePhoneAction(State s)
        {
            PhoneAction pa = s.Action as PhoneAction;
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            var intType = pa.ActionType == PhoneAction.PhoneActionType.TALK ? InteractionType.TALK : InteractionType.TEXT;

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(intType, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
            {
                if (pa.ActionType == PhoneAction.PhoneActionType.TALK)
                    yield return characterControl.TalkLeft();
                else
                    yield return characterControl.TextLeft();
            }
            else if (intHand == FullBodyBipedEffector.RightHand)
            {
                if (pa.ActionType == PhoneAction.PhoneActionType.TALK)
                    yield return characterControl.TalkRight();
                else
                    yield return characterControl.TextRight();
            }
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteSwitchOnPrefab(State s)
        {
            SwitchOnAction ga = (SwitchOnAction)s.Action;
            GameObject go = s.GetScriptGameObject(ga.Name);

            if (go.GetComponent<HandInteraction>() != null)
            {
                HandInteraction hi = go.GetComponent<HandInteraction>();
                if (cameraControls != null)
                {
                    for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                        if (cameraControls[cam_id] != null)
                            cameraControls[cam_id].SetFocusObject(go);
                }
                recorder.MarkActionStart(ga.Off ? InteractionType.SWITCHOFF : InteractionType.SWITCHON, ga.ScriptLine);
                yield return characterControl.StartInteraction(go, (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND"),
                    hi.SwitchIndex(HandInteraction.ActivationAction.SwitchOn));

                if (cameraControls != null)
                {
                    for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                        if (cameraControls[cam_id] != null)
                            cameraControls[cam_id].ClearFocusObject();
                }
            }
        }

        private IEnumerator ExecutePut(State s)
        {
            IAction ga = s.Action;
            GameObject go = s.GetScriptGameObject(ga.Name);
            Vector3 putPosition = (Vector3)s.GetObject("PUT_POSITION");
            var goi = go.GetComponent<HandInteraction>().invisibleCpy;
            Bounds focusBounds;

            if (s.Action is PutAction)
            {
                GameObject goDest = s.GetScriptGameObject((s.Action as PutAction).DestName);
                focusBounds = GameObjectUtils.GetBounds(goDest);
            }
            else
            {
                focusBounds = GameObjectUtils.GetBounds(go);
            }
            goi.transform.position = putPosition;
            recorder.MarkActionStart(InteractionType.PUTBACK, ga.ScriptLine);

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetFocusObject(go, new Bounds(putPosition, focusBounds.size));
            }
            yield return characterControl.GrabObject(goi, (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND"));
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
            }
            ClearFocusObject();
        }

        private IEnumerator ExecutePutBack(State s)
        {
            yield return ExecuteWalkOrRun(s, false, s.Action.Name);
            yield return ExecutePut(s);
        }

        private IEnumerator ExecuteTouch(State s)
        {
            TouchAction a = (TouchAction)s.Action;
            GameObject go = s.GetScriptGameObject(a.Name);

            var hi = go.AddComponent<HandInteraction>();
            var sw = new HandInteraction.ActivationSwitch(HandInteraction.HandPose.Button,
                HandInteraction.ActivationAction.SwitchOn, Vector3.zero, null);
            hi.switches = new List<HandInteraction.ActivationSwitch>();
            hi.switches.Add(sw);
            hi.allowPickUp = true;
            hi.added_runtime = true;
            hi.grabHandPose = GetGrabPose(go).Value; // HandInteraction.HandPose.GrabVertical;
            hi.Initialize();

            recorder.MarkActionStart(InteractionType.TOUCH, a.ScriptLine);

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetFocusObject(go);
            }
            yield return characterControl.StartInteraction(go, (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND"));
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearFocusObject();
            }
        }

        private IEnumerator ExecuteBrush(State s)
        {
            //Debug.Log("Execute Brush...");
            //yield return new WaitForSeconds(0.5f);
            //recorder.MarkActionStart(InteractionType.BRUSH, s.Action.ScriptLine);
            //yield return characterControl.StartCoroutine(characterControl.Brush());

            Debug.Log("I am at ExcuteBrush in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.BrushLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.BrushRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteCut(State s)
        {
            Debug.Log("I am at ExcuteCut in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.CutLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.CutRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteEat(State s)
        {
            Debug.Log("I am at ExcuteEat in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.EatLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.EatRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteFold(State s)
        {
            Debug.Log("Execute Fold...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.FOLD, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Fold());
        }

        private IEnumerator ExecuteJump(State s)
        {
            Debug.Log("Execute Jump...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.JUMP, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Jump());
        }

        private IEnumerator ExecuteJumpUp(State s)
        {
            Debug.Log("Execute JumpUp...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.JUMP, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.JumpUp());
        }

        private IEnumerator ExecuteJumpDown(State s)
        {
            Debug.Log("Execute JumpDown...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.JUMP, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.JumpDown());
        }

        private IEnumerator ExecuteKneel(State s)
        {
            Debug.Log("Execute Kneel...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.KNEEL, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Kneel());
        }

        private IEnumerator ExecuteLift(State s)
        {
            Debug.Log("Execute Lift...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.LIFT, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Lift());
        }

        private IEnumerator ExecuteDrop(State s)
        {
            Debug.Log("I am at ExcuteDrop in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.DropLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.DropRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteRinse(State s)
        {
            Debug.Log("I am at ExcuteRinse in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.RinseLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.RinseRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteSquat(State s)
        {
            Debug.Log("Execute Squat...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.SQUAT, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Squat());
        }

        private IEnumerator ExecuteSqueeze(State s)
        {
            Debug.Log("I am at ExcuteSqueeze in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.SqueezeLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.SqueezeRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteStretch(State s)
        {
            Debug.Log("Execute Stretch...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.STRETCH, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Stretch());
        }

        private IEnumerator ExecuteSweep(State s)
        {
            Debug.Log("I am at ExcuteSweep in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.SweepLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.SweepRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        /*
        private IEnumerator ExecutePutOn(State s)
        {
            Debug.Log("Execute PutOn...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.PUTON, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.PutOn());
        }

        private IEnumerator ExecutePutOff(State s)
        {
            Debug.Log("Execute PutOff...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.PUTOFF, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.PutOff());
        }
        */
        private IEnumerator ExecuteStir(State s)
        {
            Debug.Log("I am at ExcuteStir in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.StirLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.StirRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteThrow(State s)
        {
            Debug.Log("Execute Throw...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.THROW, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Throw());
        }

        private IEnumerator ExecuteType(State s)
        {
            Debug.Log("Execute Type...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.TYPE, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Type());
        }

        private IEnumerator ExecuteUnFold(State s)
        {
            Debug.Log("Execute UnFold...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.UNFOLD, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.UnFold());
        }

        private IEnumerator ExecuteVacuum(State s)
        {
            Debug.Log("Execute Vacuum...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.VACUUM, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Vacuum());
        }

        private IEnumerator ExecuteWakeUp(State s)
        {
            Debug.Log("Execute WakeUp...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.WAKEUP, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.WakeUp());
        }

        private IEnumerator ExecuteWipe(State s)
        {
            Debug.Log("I am at ExcuteWipe in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.WipeLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.WipeRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

       private IEnumerator ExecuteWrap(State s)
        {
            Debug.Log("I am at ExcuteWrap in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.WrapLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.WrapRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteWrite(State s)
        {
            Debug.Log("Execute Write...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.WRITE, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Write());
        }

        private IEnumerator ExecuteFall(State s)
        {
            Debug.Log("Execute Fall...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.FALL, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Fall());
        }

        private IEnumerator ExecuteFallSit(State s)
        {
            Debug.Log("Execute FallSit...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.FALL, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.FallSit());
        }

        private IEnumerator ExecuteFallTable(State s)
        {
            Debug.Log("Execute FallTable...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.FALL, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.FallTable());
        }

        private IEnumerator ExecuteFallBack(State s)
        {
            Debug.Log("Execute FallBack...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.FALL, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.FallBack());
        }

        /*
        private IEnumerator ExecuteStand(State s)
        {
            Debug.Log("Execute Stand...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.STAND, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Stand());
        }
        */
        private IEnumerator ExecuteStraddle(State s)
        {
            Debug.Log("Execute Straddle...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.STRADDLE, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Straddle());
        }

        private IEnumerator ExecuteLegOpp(State s)
        {
            Debug.Log("Execute LegOpp...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.LEGOPP, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.LegOpp());
        }

        /*
        private IEnumerator ExecuteStandWith(State s)
        {
            Debug.Log("Execute StandWith...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.STANDWITH, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.StandWith());
        }
    
        private IEnumerator ExecuteWalkWith(State s)
        {
            Debug.Log("Execute WalkWith...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.WALKWITH, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.WalkWith());
        }
        */

        private IEnumerator ExecuteScrub(State s)
        {
            Debug.Log("I am at ExcuteScrub in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.ScrubLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.ScrubRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }
        /*
        private IEnumerator ExecuteSew(State s)
        {
            Debug.Log("Execute Sew...");
            yield return new WaitForSeconds(0.5f);
            recorder.MarkActionStart(InteractionType.SEW, s.Action.ScriptLine);
            yield return characterControl.StartCoroutine(characterControl.Sew());
        }
        */
        private IEnumerator ExecuteShake(State s)
        {
            Debug.Log("I am at ExcuteShake in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.ShakeLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.ShakeRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteSmell(State s)
        {
            Debug.Log("I am at ExcuteSmell in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.SmellLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.SmellRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecuteSoak(State s)
        {
            Debug.Log("I am at ExcuteSoak in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.SoakLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.SoakRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }

        private IEnumerator ExecutePour(State s)
        {
            Debug.Log("I am at ExcutePour in ScriptExcution.cs");
            var intHand = (FullBodyBipedEffector)s.GetObject("INTERACTION_HAND");
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].SetVisibleArea(characterControl.UpperPartArea());
            }
            recorder.MarkActionStart(InteractionType.WIPE, s.Action.ScriptLine);
            if (intHand == FullBodyBipedEffector.LeftHand)
                yield return characterControl.PourLeft();
            else if (intHand == FullBodyBipedEffector.RightHand)
                yield return characterControl.PourRight();

            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearVisibleArea();
            }
        }
    

        #endregion

        void ClearFocusObject()
        {
            if (cameraControls != null)
            {
                for (int cam_id = 0; cam_id < cameraControls.Count; cam_id++)
                    if (cameraControls[cam_id] != null)
                        cameraControls[cam_id].ClearFocusObject();
            }
        }

        public static IEnumerator<T> ExceptionSafeEnumerator<T>(IEnumerator<T> enumerator)
        {
            bool haveNext;

            do
            {
                try
                {
                    haveNext = enumerator.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Exception thrown: " + e.Message);
                    haveNext = false;
                }
                if (haveNext)
                    yield return enumerator.Current;
            } while (haveNext);
        }

        public static IEnumerator ExceptionSafeEnumerator(IEnumerator enumerator)
        {
            bool haveNext;

            do
            {
                try
                {
                    haveNext = enumerator.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Exception thrown: " + e.Message);
                    haveNext = false;
                }
                if (haveNext)
                    yield return enumerator.Current;
            } while (haveNext);
        }


        // Prepare scene, process & execute script
        public IEnumerator ProcessAndExecute(bool recording, TestDriver caller)
        {
            this.caller = caller;
            report.Reset();
            IEnumerator<StateList> enumerator = ExceptionSafeEnumerator(Process(script).GetEnumerator());
            IEnumerator result;
            if (enumerator.MoveNext())
            {
                //processStopwatch.Stop();
                //Debug.Log(String.Format("process time: {0}", processStopwatch.ElapsedMilliseconds));
                report.ResetProcessingMessage();
                if (!SkipExecution)
                {
                    if (this.find_solution)
                    {
                        PrepareSceneForScript(enumerator.Current, recorder.saveSceneStates);
                    }
                    System.Diagnostics.Stopwatch prepareStopwatch = System.Diagnostics.Stopwatch.StartNew();
                    if (recording)
                    {
                        recorder.MaxFrameNumber = EstimateFrameNumber(enumerator.Current);
                        recorder.Recording = true;
                    }
                    prepareStopwatch.Stop();
                    Debug.Log(String.Format("prepare time: {0}", prepareStopwatch.ElapsedMilliseconds));
                    //yield return ExceptionSafeEnumerator(Execute(enumerator.Current));
                    result = ExceptionSafeEnumerator(Execute(enumerator.Current));

                    yield return result;
                }
            }
            else
            {
                //ClearScript();
                report.AddItem("EXECUTION_GENERAL", "Script is impossible to execute");
            }
            caller.finishedChars++;
        }

        private int EstimateFrameNumber(StateList current)
        {
            const int GOTO_MAX_NUMBER = 800;
            const int OTHER_MAX_NUMBER = 120;

            int result = 0;

            foreach (State s in current)
            {
                if (s.Action is GotoAction) result += GOTO_MAX_NUMBER;  // TODO: Temp. solution, maybe move limit to Action interface
                else result += OTHER_MAX_NUMBER;
            }
            if (result == 0)
                result = GOTO_MAX_NUMBER;
            return result;
        }

        private IEnumerator Execute(StateList sl)
        {
            foreach (State s in sl)
            {
                yield return s.ActionMethod(s);
                if (recorder.BreakExecution())
                    break;
            }
        }

        private IEnumerable<StateList> Process(List<ScriptPair> script)
        {
            StateList initSl = new StateList();
            if (caller.CurrentStateList.Count <= this.charIndex)
            {
                Debug.LogError("Invalid character index");
            }

            if (caller.CurrentStateList[this.charIndex] == null)
            {
                initSl.Add(new State(characterControl.transform.position));
            }
            else
            {
                initSl.Add(new State(caller.CurrentStateList[this.charIndex],
                                 characterControl.transform.position));
            }

            gotoExecDepth = 0;
            gotoExecDepth = 0;
            Debug.Log("Process...");
            Debug.Log(this.charIndex);
            execStartTime.Start();
            IEnumerable<StateList> path = ProcessRec(script, 0, initSl);
            execStartTime.Reset();
            return path;

        }

        private IEnumerable<StateList> ProcessRec(List<ScriptPair> spl, int spli, StateList sl)
        {
            if (spli >= spl.Count || sl.Count == 0)
            {
                caller.CurrentStateList[this.charIndex] = sl.Last();
                yield return sl;
            }
            else
            {
                ScriptPair sp = spl[spli];
                State last = sl.Last();

                if (sp.Action is GotoAction && gotoExecDepth < sl.Count)
                    gotoExecDepth = spli;
                foreach (IStateGroup sg in sp.ProcessMethod(sp.Action, last))
                {
                    StateList appSl = new StateList(sl);

                    appSl.Add(sg);
                    foreach (var newSl in ProcessRec(spl, spli + 1, appSl))
                    {
                        yield return newSl;
                    }
                    if (/*s.Action is GotoAction && spli < gotoExecDepth - 1 || */ execStartTime.ElapsedMilliseconds > processingTimeLimit)
                    {
                        break;
                    }
                }
            }
        }

        // Count Goto actions in spl[i + 1..j - 1]
        private int CountGotoActions(List<ScriptPair> spl, int i, int j)
        {
            int count = 0;

            for (int k = i + 1; k < j && k < spl.Count; k++)
            {
                if (spl[k].Action is GotoAction)
                    count++;
            }
            return count;
        }

        #region Utilities

        private static HashSet<GameObject> ObjectsInHands(State s)
        {
            HashSet<GameObject> result = new HashSet<GameObject>();
            GameObject go;

            go = s.GetGameObject("RIGHT_HAND_OBJECT");
            if (go != null) result.Add(go);
            go = s.GetGameObject("LEFT_HAND_OBJECT");
            if (go != null) result.Add(go);
            return result;
        }

        private static bool IsGrabbable(GameObject go)
        {
            return GetGrabPose(go) != null;
        }

        private static bool IsTouchable(Vector3 intPos, GameObject go, out Vector3 pos)
        {
            Collider co = GameObjectUtils.GetCollider(go);
            Vector3 src = new Vector3(intPos.x, 1.5f, intPos.z);

            pos = co.ClosestPointOnBounds(src);
            Vector3 dir = pos - src;
            return new Vector2(dir.x, dir.z).magnitude < 2f && pos.y < 2;
        }

        private bool IsOpenable(Vector3 intPos, ScriptObjectData sod, out IList<Vector3> switchPos, bool useIntPos, InteractionType intType)
        {
            if (ScriptObjectUtils.CanOpenOrClose(sod, intType))
            {
                return IsOpenablePosition(intPos, sod.GameObject, out switchPos, useIntPos, intType == InteractionType.CLOSE);
            }
            else
            {
                switchPos = new List<Vector3>();
                return false;
            }
        }

        // Check for openable sod, if not null, else check go, go should not be sod.GameObject
        private bool IsOpenable(Vector3 intPos, ScriptObjectData sod, GameObject go, out IList<Vector3> switchPos, bool useIntPos, InteractionType intType)
        {
            if (sod != null) return IsOpenable(intPos, sod, out switchPos, useIntPos, intType);
            else
            {
                // Newly discovered -- check what is the initial status of objects:
                // Initially, doors can be closed or opened, other things, if openable, 
                // are closed by default and can only be closed
                if (go.GetComponent<Properties_door>() == null && intType == InteractionType.CLOSE)
                {
                    switchPos = new List<Vector3>();
                    return false;
                }
                else
                {
                    return IsOpenablePosition(intPos, go, out switchPos, useIntPos, intType == InteractionType.CLOSE);
                }
            }
        }

        // Check if go is openable from intPos. If useIntPos = true also check distance from switchPos to intPos
        private bool IsOpenablePosition(Vector3 intPos, GameObject go, out IList<Vector3> switchPos, bool useIntPos, bool close)
        {
            Vector3 src = new Vector3(intPos.x, 1.5f, intPos.z);
            switchPos = new List<Vector3>();
            Properties_door pd = go.GetComponent<Properties_door>();

            if (pd == null)
            {
                // Not door
                HandInteraction hi = go.GetComponent<HandInteraction>();

                if (hi != null)
                {
                    int switchIndex = hi.SwitchIndex(HandInteraction.ActivationAction.Open);

                    if (switchIndex >= 0)
                    {
                        Vector3 pos = hi.switches[switchIndex].switchPosition;
                        pos = hi.switches[switchIndex].switchTransform.TransformPoint(pos);
                        pos += 0.75f * GetObjectFrontVec(go);
                        if (!useIntPos || (pos - src).magnitude < 1.5f)
                        {
                            switchPos.Add(pos);
                        }
                    }
                }
            }
            else
            {
                List<Vector3> potentialPos = new List<Vector3>();
                if (close)
                {
                    Vector3[] centers = new Vector3[] {
                        (pd.transformIKClose.position + 0.5f * go.transform.right),
                        (pd.transformIKClose.position - 0.5f * go.transform.right)
                    };
                    foreach (Vector3 pos in centers)
                    {
                        potentialPos.AddRange(CalculateInteractionPositions(pos, go, 1, 10, 0, 0.25f, 0.25f));
                    }
                }
                else
                {
                    Vector3 offset = new Vector3(0.0f, pd.transform.position.y);
                    potentialPos.Add(pd.posItrnPush + offset);
                    potentialPos.Add(pd.posItrnPull + offset);
                }

                foreach (Vector3 pos in potentialPos)
                {
                    if ((!useIntPos || (pos - src).magnitude < 1.5f /* !!! */))
                    {
                        switchPos.Add(pos);
                    }
                }
            }
            return switchPos.Count > 0;
        }

        private static bool IsSwitchable(Vector3 intPos, GameObject go, out Vector3 switchPos, bool useIntPos)
        {
            Vector3 src = new Vector3(intPos.x, 1.5f, intPos.z);
            HandInteraction hi = go.GetComponent<HandInteraction>();
            //Vector3 front_vec = GetObjectFrontVec(go);
            if (hi != null)
            {
                int switchIndex = hi.SwitchIndex(HandInteraction.ActivationAction.SwitchOn);

                if (switchIndex >= 0)
                {
                    Vector3 pos = hi.switches[switchIndex].switchPosition;
                    if (hi.switches[switchIndex].switchTransform != null)
                        pos = hi.switches[switchIndex].switchTransform.TransformPoint(pos);
                    else
                        pos = go.transform.TransformPoint(pos);
                    if (!useIntPos || (pos - src).magnitude < 1.5f)
                    {
                        switchPos = pos;
                        return true;
                    }
                }
            }
            switchPos = Vector3.zero;
            return false;
        }

        private static Vector3 GetObjectFrontVec(GameObject go)
        {
            // TODO: put this in a separate json file
            Vector3 front_vec = go.transform.right;
            if (go.name.StartsWith("Microwave_1") || go.name.StartsWith("Cabinet_1"))
            {
                front_vec = -go.transform.up;
            }
            else if (go.name == "Cabinet_2" || go.name.Contains("Microwave") || go.name == "mH_FloorCabinet01")
            {
                front_vec = go.transform.forward;
            }
            else if (go.name.Contains("APP_Toaster"))
            {
                front_vec = -go.transform.right;
            }


            return front_vec;
        }

        public static HandInteraction.HandPose? GetGrabPose(GameObject go)
        {
            if (go == null)
                return null;

            Collider co = GameObjectUtils.GetCollider(go);

            if (co == null)
                return null;

            Vector3 size = co.bounds.size;

            if (size.x < 0.15f && size.y < 1.0f && size.z < 0.15f) return HandInteraction.HandPose.GrabVertical; // vertical "cylinder"
            // else if (size.x < 0.4f && size.z < 0.4f && size.y < 0.5) return HandInteraction.HandPose.GrabHorizontal; // flat object
            // else return null;
            else return HandInteraction.HandPose.GrabHorizontal;
        }

        private List<Vector3> CalculateInteractionPositions(GameObject go, IInteractionArea area, bool ignore_visiblity = false)
        {
            return CalculateInteractionPositions(go.transform.position, go, int.MaxValue, 20, 0.25f, 4.5f, 0.25f, ignore_visiblity);
        }

        private List<Vector3> CalculateInteractionPositions(Vector3 initPos, GameObject go, int maxPos,
            int angles, float rMin, float rMax, float rStep, bool ignore_visibility = false)
        {
            const float ObstructionHeight = 0.1f;   // Allow for some obstuction around target object (e.g., carpet)

            NavMeshAgent nma = characterControl.GetComponent<NavMeshAgent>();
            List<Vector3> result = new List<Vector3>();

            if (nma == null)
                return result;
            rMax += rStep / 2.0f;
            for (int i = 0; i < angles; i++)
            {
                float phi = 2 * Mathf.PI * i / angles;

                for (float r = rMin; r < rMax; r += rStep)
                {
                    float x = r * Mathf.Cos(phi);
                    float z = r * Mathf.Sin(phi);
                    Vector3 center = initPos + new Vector3(x, 0, z);
                    Vector3 cStart = new Vector3(center.x, nma.radius + ObstructionHeight, center.z);
                    Vector3 cEnd = new Vector3(center.x, nma.height - nma.radius + ObstructionHeight, center.z);

                    // Check for space
                    //if (go.name.ToLower().Contains("wine_glass"))
                    //{
                    //    GameObject capsulegoal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    //    capsulegoal.transform.position = (Vector3)center;
                    //    capsulegoal.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                    //    capsulegoal.GetComponent<MeshRenderer>().material.color = Color.blue;
                    //    capsulegoal.GetComponent<CapsuleCollider>().enabled = false;
                    //}
                    if (!Physics.CheckCapsule(cStart, cEnd, nma.radius * 0.75f))
                    {
                        //if (go.name.ToLower().Contains("wine_glass"))
                        //{
                        //    GameObject capsulegoal = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        //    capsulegoal.transform.position = (Vector3)center;
                        //    capsulegoal.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                        //    capsulegoal.GetComponent<MeshRenderer>().material.color = Color.green;
                        //    capsulegoal.GetComponent<CapsuleCollider>().enabled = false;
                        //}
                        if (go == null || ignore_visibility || IsVisibleFromSegment(go, center, 0.2f, 2.5f, 0.2f, true))
                        {
                            result.Add(center);
                            break; // for each angle, take the closest radius
                        }
                    }
                    
                }
                if (rMin < 0.0f) // if radius is approx. zero, take only one angle
                    rMin += rStep;
                if (result.Count >= maxPos)
                    break;
            }

            result.Sort((p1, p2) => (p1 - go.transform.position).sqrMagnitude.CompareTo((p2 - go.transform.position).sqrMagnitude));
            // Debug.Log(string.Format("CC: Radius: {0}, Height: {1}, Center: {2}", cc.radius, cc.height, cc.center));
            // Debug.Log(string.Format("GO: Position: {0}", go.transform.position));
            // Debug.Log(string.Format("Found positions {0}", result.Count));

            return result;
        }

        private static void DebugCalcPath(string info, Vector3 from, Vector3 to)
        {
            NavMeshPath path = new NavMeshPath();
            from.y = 0;
            to.y = 0;
            NavMesh.CalculatePath(from, to, -1, path);

            Debug.Log("Go from " + from + " to: " + to + "(" + info + ") Path length " + path.corners.Length + " status: " + path.status);
            if (path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 0)
            {
                Debug.Log("Path: " + string.Join("->", path.corners));
            }
        }

        // Checks if GameObject go is visible from position v
        // Sends one ray from go to v.
        public static bool IsVisible(GameObject go, Vector3 v)
        {
            return IsVisible(go, Vector3.zero, v);
        }

        // Checks if GameObject go is visible from position v
        // Sends one ray from go + goDelta to v.
        public static bool IsVisible(GameObject go, Vector3 goDelta, Vector3 v)
        {
            RaycastHit hit;
            Vector3 direction = go.transform.position + goDelta - v;
            bool rcResult = Physics.Raycast(v, direction, out hit, direction.magnitude);

            return !rcResult || GameObjectUtils.IsInPath(go, hit.transform);
        }

        // Checks if GameObject go is visible from position v
        // Checks randomly selected nRays from collider of go
        // If there is no colider attached to go or to go's children, IsVisible is returned
        public static float VisiblityFactor(GameObject go, Vector3 v, int nRays = 10, bool show_rays = false)
        {
            Bounds bounds = GameObjectUtils.GetBounds(go);

            if (bounds.extents == Vector3.zero)
            {
                bool visible = IsVisible(go, v);
                return visible ? 1.0f / nRays : 0.0f;
            }
            else
            {
                RaycastHit hit;
                int hitCount = 0;

                for (int rn = 0; rn < nRays; rn++)
                {
                    float rx = UnityEngine.Random.Range(-bounds.extents.x, bounds.extents.x);
                    float ry = UnityEngine.Random.Range(-bounds.extents.y, bounds.extents.y);
                    float rz = UnityEngine.Random.Range(-bounds.extents.z, bounds.extents.z);

                    Vector3 direction = bounds.center + new Vector3(rx, ry, rz) - v;

                    //if (show_rays && direction.magnitude < 2.0f)
                    //{
                    //    Debug.DrawRay(v, direction, Color.red, 10.0f);
                    //}

                    bool rcResult = Physics.Raycast(v, direction, out hit, direction.magnitude);
                    //if (show_rays && !hit.transform.parent.parent.gameObject.name.ToLower().Contains("cabinet"))
                    //{
                    //    GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    //    capsule.transform.position = hit.point;
                    //    capsule.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
                    //    capsule.GetComponent<CapsuleCollider>().enabled = false;
                    //    Debug.Log(hit.transform.parent.parent.gameObject.name);
                    //}
                    if (!rcResult || GameObjectUtils.IsInPath(go, hit.transform))
                        hitCount++;
                }
                return (float)hitCount / nRays;
            }
        }

        public static void DrawLine(Vector3 start, Vector3 end, float duration = 10.2f)
        {
            Color color = Color.red;
            GameObject myLine = new GameObject();
            myLine.transform.position = start;
            myLine.AddComponent<LineRenderer>();
            LineRenderer lr = myLine.GetComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Particles/Alpha Blended Premultiply"));
            lr.startColor = color;
            lr.endColor = color;
            lr.startWidth = 0.1f;
            lr.endWidth = 0.1f;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
            GameObject.Destroy(myLine, duration);
        }

        // Checks if GameObject go is visible from position v
        // Checks nRays to randomly selected points from the vertical axis of the collider
        // If there is no colider attached, 0 is returned
        public static float AxisVisiblityFactor(GameObject go, Vector3 v, int nRays = 15)
        {
            Collider coll = GameObjectUtils.GetCollider(go);

            if (coll == null)
            {
                return 0.0f;
            }
            else
            {
                RaycastHit hit;
                Bounds b = coll.bounds;
                int hitCount = 0;
                float ry = -b.extents.y;
                float ydelta = 2 * b.extents.y / nRays;

                for (int rn = 0; rn <= nRays; rn++)
                {
                    // float ry = UnityEngine.Random.Range(-b.extents.y, b.extents.y);
                    Vector3 direction = b.center + new Vector3(0.0f, ry, 0.0f) - v;
                    bool rcResult = Physics.Raycast(v, direction, out hit, direction.magnitude);

                    if (!rcResult || GameObjectUtils.IsInPath(go, hit.transform))
                        hitCount++;
                    ry += ydelta;
                }
                return (float)hitCount / nRays;
            }
        }


        // Return closest point or src
        public static Vector3 ClosestPoint(Vector3 src, Vector3 dir, float maxDist)
        {
            RaycastHit hit;
            bool rcResult = Physics.Raycast(src, dir, out hit, maxDist);
            return rcResult ? hit.point : src;
        }

        public static Vector3 ClosestPoint(Vector3 src)
        {
            return ClosestPoint(src, Vector3.down, 2.0f);
        }

        public static bool IsVisibleFromSegment(GameObject go, Vector3 v, float miny, float maxy, float delta = 0.1f, bool sampleGo = false)
        {
            maxy += delta / 2.0f;
            for (float y = miny; y <= maxy; y += delta)
            {
                bool show_rays = false;
                if (go.name.ToLower().Contains("wine_glass"))
                {
                    show_rays = true;
                }
                if (sampleGo)
                {
                    if (VisiblityFactor(go, new Vector3(v.x, y, v.z), 10, show_rays) > 0.0f)
                        return true;
                }
                else
                {
                    if (IsVisible(go, new Vector3(v.x, y, v.z)))
                        return true;
                }
            }
            return false;
        }

        // Send ray to corners and center of bounds attempting to hit go
        public static bool IsVisibleFromCorners(GameObject go, Bounds bounds, float factor = 0.9f)
        {
            foreach (Vector3 v in BoundsUtils.CornersAndCenter(bounds, factor))
            {
                if (IsVisible(go, v))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        // TODO: delete?
        private GameObject PlaceObject(GameObject destGo, string path) //, string newName)
        {
            GameObject loadedObj = Resources.Load(ScriptUtils.TransformResourceFileName(path)) as GameObject;
            Bounds destBounds = GameObjectUtils.GetBounds(destGo);
            Bounds srcBounds = GameObjectUtils.GetBounds(loadedObj);

            if (destBounds.extents == Vector3.zero)
            {
                report.AddItem("OBJECT ZERO EXTENTS", destGo.name);
                return null;
            }

            if (srcBounds.extents == Vector3.zero)
            {
                report.AddItem("LOADED OBJECT ZERO EXTENTS", path);
                return null;
            }

            Vector3 destMax = destBounds.max;
            Vector3 destMin = destBounds.min;
            Vector3 srcCenter = srcBounds.center;
            int maxTries = randomizeExecution ? 10 : 5;
            Vector3 centerDelta = srcCenter - loadedObj.transform.position;

            for (int tryCount = 0; tryCount < maxTries; tryCount++)
            {
                float x, z;

                if (randomizeExecution)
                {
                    x = UnityEngine.Random.Range(destMin.x, destMax.x);
                    z = UnityEngine.Random.Range(destMin.z, destMax.z);
                }
                else
                {
                    x = Mathf.Lerp(destMin.x, destMax.x, (float)(tryCount + 0.5f) / maxTries);
                    z = Mathf.Lerp(destMin.z, destMax.z, (float)(tryCount + 0.5f) / maxTries);
                }

                //bool ok = (Mathf.Abs(x - destMin.x) > 0.1f && Mathf.Abs(z - destMin.z) > 0.1f && Mathf.Abs(x - destMax.x) > 0.1f && Mathf.Abs(z - destMax.z) > 0.1f) &&
                //    !(Mathf.Abs(x - destMin.x) > 1 && Mathf.Abs(z - destMin.z) > 1 && Mathf.Abs(x - destMax.x) > 1 && Mathf.Abs(z - destMax.z) > 1f);

                //if (!ok)
                //    continue;

                Vector3 srcDelta = new Vector3(x - srcCenter.x, destMax.y - srcBounds.min.y, z - srcCenter.z);
                float hity;

                if (GameObjectUtils.HitFlatSurface(srcBounds, srcDelta, destGo, out hity))
                {
                    float yDelta = hity - (srcBounds.min.y + srcDelta.y);
                    Vector3 delta = srcDelta + new Vector3(0, yDelta, 0);

                    if (!GameObjectUtils.CheckBox(srcBounds, delta, 0.01f, destGo))
                    {
                        string newName = loadedObj.name;
                        GameObject newGo = MonoBehaviour.Instantiate(loadedObj, destGo.transform) as GameObject;
                        // Rename the GameObject so that its class can be identified (number with parentheses append to its original name when instantiated)
                        newGo.name = loadedObj.name;
                        ColorEncoding.EncodeGameObject(newGo);

                        newGo.transform.position = srcBounds.center + delta - centerDelta;
                        //newGo.transform.position = srcBounds.center + delta; // += delta - destGo.transform.position; // += delta; // Translate(srcDelta + new Vector3(0, yDelta, 0));
                        //var tmpBounds = GameObjectUtils.GetBounds(newGo);
                        //Debug.Log($"Bounds {tmpBounds.center.x}, {tmpBounds.center.y}, {tmpBounds.center.z} vs {x}, y, {z}");
                        //Debug.Log($"SrcBounds {srcBounds.center.x}, {srcBounds.center.y}, {srcBounds.center.z} / delta {delta.x}, y, {delta.z}");
                        return newGo;
                    }
                }
            }
            report.AddItem("NO SPACE", path, destGo.name);
            return null;
        }

        // Prepare scene for the script.
        // Place here anything that is needed for correct processing of actions
        // Current: 
        //   - Prepare State_object component if necessary
        //   - Switch off light which is to be turned on by some action (we assume all lights are turned on by default)
        private void PrepareSceneForScript(StateList sl, bool addStateObject)
        {
            HashSet<GameObject> processedGOs = new HashSet<GameObject>();
            HashSet<GameObject> processedSwtchGOs = new HashSet<GameObject>();
            foreach (State s in sl)
            {
                // If addStateObject is true, we are generating SceneState GTs. Thus we need to
                // prepare State_object component. 
                if (addStateObject && s.Action != null)
                {
                    GameObject go = s.GetScriptGameObject(s.Action.Name);
                    if (go != null && !processedGOs.Contains(go))
                    {
                        processedGOs.Add(go);
                    }
                }

                // If this action is switching on a light switch, it must be turned off in the beginning
                SwitchOnAction sa = s.Action as SwitchOnAction;
                if (sa != null && !sa.Off)
                {
                    GameObject go = s.GetScriptGameObject(sa.Name);
                    if (go != null && !processedSwtchGOs.Contains(go))
                    {
                        processedSwtchGOs.Add(go);
                        HandInteraction hi = go.GetComponent<HandInteraction>();
                        if (hi != null && go.name.Contains("Light_switch"))
                        {
                            hi.switches[0].FlipInitialState();
                        }
                    }
                }
                OpenAction oa = s.Action as OpenAction;
                if (oa != null && s.Previous != null)
                {
                    ScriptObjectData sod;

                    if (s.Previous.GetScriptGameObjectData(oa.Name, out sod))
                    {
                        Properties_door pd = sod.GameObject.GetComponent<Properties_door>();

                        if (pd != null && !oa.Close)
                        {
                            pd.SetDoorToClose();
                        }
                    }
                }
            }

            // Preparing must come after flipping initial state because it updates Recorder's SceneStateSequence
            // with current object's state and we want to use flipped value, not the initial value.
            if (addStateObject)
            {
                foreach (GameObject go in processedGOs)
                {
                    State_object so = go.GetComponent<State_object>();
                    if (so == null)
                    {
                        so = go.AddComponent<State_object>();
                        so.Initialize();
                    }
                    so.Prepare(recorder.sceneStateSequence);
                }
            }
        }

        public string CreateReportString()
        {
            return report.ToString();
        }

        internal IObjectSelector GetRoomOrObjectSelector(string name, int instance)
        {
            if (roomSelector.IsRoomName(name) && this.find_solution)
            {
                return roomSelector.GetRoomSelector(name);
            }
            else
            {
                return objectSelectorProvider.GetSelector(name, instance);
            }
        }

        internal IObjectSelector GetObjectSelector(string name, int instance)
        {
            return objectSelectorProvider.GetSelector(name, instance);
        }
    }


    public class ScriptReaderException : Exception
    {
        public ScriptReaderException(string message) :
            base(message)
        {
        }
    }

    public class ScriptLine
    {
        public InteractionType Interaction { get; set; }
        public IList<Tuple<string, int>> Parameters { get; set; }
        public IList<float> modifier { get; set; }
        public int LineNumber { get; set; }

        internal bool CompareParameters(ScriptLine otherSl)
        {
            return Parameters.SequenceEqual(otherSl.Parameters);
        }
    }

    public class ScriptChecker
    {
        public static List<Tuple<int, Tuple<String, String>>> SolveConflicts(List<ScriptExecutor> sExecutors)
        {

            // Solve conflicts when multiple agents are trying to open/grab the same object
            Dictionary<int, List<int>> dict_conflicts = new Dictionary<int, List<int>>();
            Dictionary<int, InteractionType> action_conflicts = new Dictionary<int, InteractionType> ();
            List < Tuple<int, Tuple<String, String>> > conflict_messages = new List<Tuple<int, Tuple<String, String>>>();
            for (int i = 0; i < sExecutors.Count(); i++)
            {
                for (int script_index = 0; script_index < sExecutors[i].sLines.Count(); script_index++)
                {
                    if (sExecutors[i].sLines[script_index].Interaction == InteractionType.OPEN ||
                        sExecutors[i].sLines[script_index].Interaction == InteractionType.CLOSE ||
                        sExecutors[i].sLines[script_index].Interaction == InteractionType.GRAB){
                        int index_object = sExecutors[i].sLines[script_index].Parameters[0].Item2;
                        if (!dict_conflicts.ContainsKey(index_object))
                        {

                            dict_conflicts[index_object] = new List<int>();
                            action_conflicts[index_object] = sExecutors[i].sLines[script_index].Interaction;
                        }
                        
                        dict_conflicts[index_object].Add(i);
                    }
                }
            }

            foreach (KeyValuePair<int, List<int>> kvp in dict_conflicts)
            {
                if (kvp.Value.Count() > 1)
                {
                    InteractionType conflict_action = action_conflicts[kvp.Key];
                    int index_char_perform = RandomUtils.Choose(kvp.Value);
                    for (int i = 0; i < kvp.Value.Count(); i++)
                    {
                        int index_char = kvp.Value[i];
                        if (index_char != index_char_perform)
                        {
                            sExecutors[index_char].script.Clear();
                            sExecutors[index_char].sLines.Clear();
                            Tuple<String, String> ct = new Tuple<String, String> ("PROCESS UNDEF", $"Agent {index_char_perform} tried to do the same action");
                            if (conflict_action == InteractionType.OPEN)
                               ct = new Tuple<String, String>("PROCESS OPEN", $"Agent {index_char_perform} tried to open the object at the same time");

                            if (conflict_action == InteractionType.CLOSE)
                               ct = new Tuple<String, String>("PROCESS CLOSE", $"Agent {index_char_perform} tried to open the object at the same time");


                            if (conflict_action == InteractionType.GRAB)
                                ct = new Tuple<String, String>("PROCESS GRAB", $"Agent {index_char_perform} tried to grab the object at the same time");


                            conflict_messages.Add(new Tuple<int, Tuple<string, string>>(index_char, ct));
                        }
                    }
                }
            }
            return conflict_messages;
        }
    }

    public class ScriptReader
    {

        public static void ParseScript(List<ScriptExecutor> sExecutors, IList<string> scriptLines,
            ActionEquivalenceProvider actionEquivProvider)
        {
            // Add for check
            Debug.Log("sExcuters counts = " + sExecutors.Count + " at ParseScript in Execution.cs");
            for (int i = 0; i < sExecutors.Count; i++)
            {
                ParseScriptForChar(sExecutors[i], scriptLines, i, actionEquivProvider);
            }
        }

        private static void ParseScriptForChar(ScriptExecutor sExecutor, IList<string> scriptLines, int charIndex,
            ActionEquivalenceProvider actionEquivProvider)
        {
            IList<ScriptLine> sLines = new List<ScriptLine>();

            for (int lineNo = 0; lineNo < scriptLines.Count; lineNo++)
            {
                string line = scriptLines[lineNo];
                ScriptLine sl = ParseLineForChar(charIndex, line, lineNo, actionEquivProvider);

                if (sl != null)
                    sLines.Add(sl);
            }
            sExecutor.sLines = new List<ScriptLine>(sLines);
            for (int i = 0; i < sLines.Count; i++)
            {
                ScriptLineToAction(sExecutor, i, sLines);
            }
        }

        private static void ScriptLineToAction(ScriptExecutor sExecutor, int index, IList<ScriptLine> sLines)
        {
            ScriptLine sl = sLines[index];
            ScriptLine nextSl = index + 1 < sLines.Count ? sLines[index + 1] : null;
            InteractionType nextIt = nextSl != null && sl.CompareParameters(nextSl) ? nextSl.Interaction : InteractionType.UNSPECIFIED;
            string name0 = sl.Parameters.Count == 0 ? "" : sl.Parameters[0].Item1;
            int instance0 = sl.Parameters.Count == 0 ? 0 : sl.Parameters[0].Item2;
            string name1 = sl.Parameters.Count <= 1 ? "" : sl.Parameters[1].Item1;
            int instance1 = sl.Parameters.Count <= 1 ? 0 : sl.Parameters[1].Item2;

            Debug.Log( " action type = " + sl.Interaction.ToString() + "  name0 = " + name0 + "  instance0 = " + instance0 + "  name1 = " + name1 + "  instance1 = " + instance1);

            switch (sl.Interaction)
            {
                case InteractionType.WALKFORWARD:
                    sExecutor.AddAction(new GoforwardAction(sl.LineNumber));
                    break;
                case InteractionType.TURNLEFT:
                    sExecutor.AddAction(new TurnAction(sl.LineNumber, -30.0f));
                    break;
                case InteractionType.TURNRIGHT:
                    sExecutor.AddAction(new TurnAction(sl.LineNumber, 30.0f));
                    break;
                case InteractionType.WALKTOWARDS:
                    float modif_walk_dist = 1.0f;
                    bool exists_modifier = sl.modifier.Count > 0;
                    if (exists_modifier)
                        modif_walk_dist = (float)sl.modifier[0];

                    sExecutor.AddAction(
                        new GotowardsAction(sl.LineNumber, sExecutor.GetRoomOrObjectSelector(name0, instance0),
                            name0, instance0, sl.Interaction == InteractionType.RUN, nextIt, modif_walk_dist), false);
                    break;
                
                case InteractionType.WALK:
                case InteractionType.GOTO:
                case InteractionType.RUN:
                    if (sExecutor.smooth_walk)
                        sExecutor.AddAction(
                            new GotoAction(sl.LineNumber, sExecutor.GetRoomOrObjectSelector(name0, instance0),
                                name0, instance0, sl.Interaction == InteractionType.RUN, nextIt),
                            false);
                    else
                        sExecutor.AddAction(
                        new GotowardsAction(sl.LineNumber, sExecutor.GetRoomOrObjectSelector(name0, instance0),
                            name0, instance0, sl.Interaction == InteractionType.RUN, nextIt), true);

                    break;
                case InteractionType.FIND:
                    if (sExecutor.smooth_walk)
                            sExecutor.AddAction(
                            new GotoAction(sl.LineNumber, sExecutor.GetRoomOrObjectSelector(name0, instance0),
                                name0, instance0, false, nextIt),
                            true);
                    else
                        sExecutor.AddAction(
                        new GotowardsAction(sl.LineNumber, sExecutor.GetRoomOrObjectSelector(name0, instance0),
                            name0, instance0, sl.Interaction == InteractionType.RUN, nextIt), true);
                    break;
                case InteractionType.SIT:
                    sExecutor.AddAction(new SitAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.STANDUP:
                    sExecutor.AddAction(new StandupAction(sl.LineNumber));
                    break;
                case InteractionType.WATCH:
                case InteractionType.LOOKAT:
                case InteractionType.LOOKAT_MEDIUM:
                case InteractionType.TURNTO:
                case InteractionType.POINTAT:
                    sExecutor.AddAction(new WatchAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0),
                        name0, instance0, WatchAction.MediumDuration));
                    break;
                case InteractionType.LOOKAT_SHORT:
                    sExecutor.AddAction(new WatchAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0),
                        name0, instance0, WatchAction.ShortDuration));
                    break;
                case InteractionType.LOOKAT_LONG:
                    sExecutor.AddAction(new WatchAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0),
                        name0, instance0, WatchAction.LongDuration));
                    break;
                case InteractionType.SWITCHON:
                case InteractionType.SWITCHOFF:
                    sExecutor.AddAction(new SwitchOnAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0, sl.Interaction == InteractionType.SWITCHOFF));
                    break;
                case InteractionType.GRAB:
                    sExecutor.AddAction(new GrabAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.DRINK:
                    sExecutor.AddAction(new DrinkAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.TALK:
                    sExecutor.AddAction(new PhoneAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0, PhoneAction.PhoneActionType.TALK));
                    break;
                case InteractionType.TEXT:
                    sExecutor.AddAction(new PhoneAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0, PhoneAction.PhoneActionType.TEXT));
                    break;
                case InteractionType.TOUCH:
                    sExecutor.AddAction(new TouchAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.PUT:
                case InteractionType.PUTBACK:
                case InteractionType.PUTIN:
                    if (string.IsNullOrEmpty(name1))
                        throw new ScriptReaderException($"No second argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new PutAction(sl.LineNumber, sExecutor.GetObjectSelector(name1, instance1), name0, instance0, name1, instance1, sl.Interaction == InteractionType.PUTIN));
                    break;
                case InteractionType.PUTOBJBACK:
                    sExecutor.AddAction(new PutBackAction(sl.LineNumber, name0, instance0));
                    break;
                case InteractionType.OPEN:
                    sExecutor.AddAction(new OpenAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0, false));
                    break;
                case InteractionType.CLOSE:
                    sExecutor.AddAction(new OpenAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0, true));
                    break;
                case InteractionType.BRUSH: // Add 2021
                    Debug.Log(" Brush !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new BrushAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.CUT:   // Add 2021
                    Debug.Log(" Cut !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new CutAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.EAT:   // Add 2021
                    Debug.Log(" Eat !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new EatAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.FOLD:  // Add 2021
                    Debug.Log(" Flod!!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new FoldAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.JUMP:  // Add 2021
                    Debug.Log(" Jump !!!!! ");
                    sExecutor.AddAction(new JumpAction(sl.LineNumber));
                    break;
                case InteractionType.JUMPUP:
                    Debug.Log("JumpUp");
                    sExecutor.AddAction(new JumpUpAction(sl.LineNumber));
                    break;
                case InteractionType.JUMPDOWN:
                    Debug.Log("JumpDown");
                    sExecutor.AddAction(new JumpDownAction(sl.LineNumber));
                    break;
                case InteractionType.KNEEL:  // Add 2021
                    Debug.Log(" Kneel !!!!! ");
                    sExecutor.AddAction(new KneelAction(sl.LineNumber));
                    break;
                case InteractionType.LIFT:  // Add 2021
                    Debug.Log(" Lift !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new LiftAction(sl.LineNumber));
                    break;
                case InteractionType.DROP:  // Add 2021
                    Debug.Log(" Drop !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new DropAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.RINSE:  // Add 2021
                    Debug.Log(" Rinse !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new RinseAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.SQUAT:  // Add 2021
                    Debug.Log(" Squat !!!!! ");
                    sExecutor.AddAction(new SquatAction(sl.LineNumber));
                    break;
                case InteractionType.SQUEEZE:  // Add 2021
                    Debug.Log(" Squeeze !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new SqueezeAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.STRETCH:  // Add 2021
                    Debug.Log(" Stretch !!!!! ");
                    sExecutor.AddAction(new StretchAction(sl.LineNumber));
                    break;
                case InteractionType.SWEEP:  // Add 2021
                    Debug.Log(" Sweep !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new SweepAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                //case InteractionType.PUTON:  // Add 2021
                    //break;
                //case InteractionType.PUTOFF:  // Add 2021
                    //break;
                case InteractionType.STIR:// Add 2021
                    Debug.Log(" Stir !!!!");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new StirAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.THROW:  // Add 2021
                    Debug.Log(" Throw !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new ThrowAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.TYPE:  // Add 2021
                    Debug.Log(" Type !!!!! ");
                    sExecutor.AddAction(new TypeAction(sl.LineNumber));
                    break;
                case InteractionType.UNFOLD:  // Add 2021
                    Debug.Log(" Unfold !!!!");
                    sExecutor.AddAction(new UnFoldAction(sl.LineNumber));
                    break;
                case InteractionType.VACUUM:  // Add 2021
                    Debug.Log(" Vacuum !!!!! ");
                    sExecutor.AddAction(new VacuumAction(sl.LineNumber));
                    break;
                case InteractionType.WAKEUP:  // Add 2021
                    Debug.Log(" Stir !!!!");
                    sExecutor.AddAction(new WakeUpAction(sl.LineNumber));
                    break;
                case InteractionType.WIPE: // Add 2021
                    Debug.Log(" Wipe !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    if (string.IsNullOrEmpty(name1))
                        throw new ScriptReaderException($"No second argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new WipeAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0,  name1, instance1, false));
                    break;
                case InteractionType.WRAP:  // Add 2021
                    Debug.Log(" Wrap !!!!");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new WrapAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.WRITE:  // Add 2021
                    Debug.Log(" Write !!!!! ");
                    sExecutor.AddAction(new WriteAction(sl.LineNumber));
                    break;
                case InteractionType.FALL:  // Add 2021
                    Debug.Log(" Fall !!!!! ");
                    sExecutor.AddAction(new FallAction(sl.LineNumber));
                    break;
                case InteractionType.FALLSIT:  // Add 2021
                    Debug.Log(" FallSit !!!!! ");
                    sExecutor.AddAction(new FallSitAction(sl.LineNumber));
                    break;
                case InteractionType.FALLTABLE:  // Add 2021
                    Debug.Log(" FallTable !!!!! ");
                    sExecutor.AddAction(new FallTableAction(sl.LineNumber));
                    break;
                case InteractionType.FALLBACK:  // Add 2021
                    Debug.Log(" FallBack !!!!! ");
                    sExecutor.AddAction(new FallBackAction(sl.LineNumber));
                    break;
                case InteractionType.STAND:  // Add 2021
                    //Debug.Log(" Stand !!!!! ");
                    //sExecutor.AddAction(new StandAction(sl.LineNumber));
                    break;
                case InteractionType.STRADDLE:  // Add 2021
                    Debug.Log(" Straddle !!!!! ");
                    sExecutor.AddAction(new StraddleAction(sl.LineNumber));
                    break;
                case InteractionType.LEGOPP:  // Add 2021
                    Debug.Log(" LegOpp !!!!");
                    sExecutor.AddAction(new LegOppAction(sl.LineNumber));
                    break;
                //case InteractionType.STANDWITH:  // Add 2021
                  //  break;
                //case InteractionType.WALKWITH:  // Add 2021
                  //  break;
                case InteractionType.SCRUB:  // Add 2021
                    Debug.Log(" Scrub !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    if (string.IsNullOrEmpty(name1))
                        throw new ScriptReaderException($"No second argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new ScrubAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0, name1, instance1 ));
                    break;
                //case InteractionType.SEW:  // Add 2021
                  //  break;
                case InteractionType.SHAKE:  // Add 2021
                    Debug.Log(" Shake !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new ShakeAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.SMELL:  // Add 2021
                    Debug.Log(" Smell !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new SmellAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.SOAK:  // Add 2021
                    Debug.Log(" Soak !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new SoakAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0));
                    break;
                case InteractionType.POUR:  // Add 2021
                    Debug.Log(" Pour !!!!! ");
                    //if (string.IsNullOrEmpty(name0))
                        //throw new ScriptReaderException($"No First argument for [{sl.Interaction}]");
                    if (string.IsNullOrEmpty(name1))
                        throw new ScriptReaderException($"No second argument for [{sl.Interaction}]");
                    sExecutor.AddAction(new PourAction(sl.LineNumber, sExecutor.GetObjectSelector(name0, instance0), name0, instance0, name1, instance1 ));
                    break;
                case InteractionType.SPECIAL:
                    // Ignore
                    break;

                default:
                    throw new ScriptReaderException(string.Format("Unsuported action: {0}, line: {1}", sl.Interaction.ToString(), sl.LineNumber));
            }

        }


        private static ScriptLine ParseLine(string line, int lineNo, ActionEquivalenceProvider actionEquivProvider)
        {
            // Line example: 
            // [Put] <COFFEE FILTER> (1) <COFFE MAKER> (1)

            if (string.IsNullOrEmpty(line) || line[0] != '[')
                return null;

            IList<Tuple<string, int>> paramList = new List<Tuple<string, int>>();
            IList<float> modifier = new List<float>();

            string pattAction = @"^\[(\w+)\]";
            string pattParams = @"<([\w\s]+)>\s*\((\d+)\)\s*(:\d+:)?";

            Regex r = new Regex(pattAction);
            Match m = r.Match(line);

            if (!m.Success)
                throw new ScriptReaderException(string.Format("Can not parse line {0}", line));

            string actionStr = m.Groups[1].Value;

            r = new Regex(pattParams);
            m = r.Match(line);

            while (m.Success)
            {
                paramList.Add(Tuple.Create(m.Groups[1].Value, int.Parse(m.Groups[2].Value)));
                if (m.Groups.Count > 2)
                {
                    modifier.Add(float.Parse(m.Groups[3].Value));
                }
                m = m.NextMatch();
            }

            if (paramList.Count == 1)
            {
                string newActionStr;

                if (actionEquivProvider.TryGetEquivalentAction(actionStr, paramList[0].Item1, out newActionStr))
                    actionStr = newActionStr;
            }
            else if (paramList.Count == 2)
            {
                string newActionStr;

                if (actionEquivProvider.TryGetEquivalentAction(actionStr, paramList[0].Item1, paramList[1].Item1, out newActionStr))
                    actionStr = newActionStr;
            }

            InteractionType action = (InteractionType)Enum.Parse(typeof(InteractionType), actionStr, true);

            return new ScriptLine() { Interaction = action, Parameters = paramList, LineNumber = lineNo };
        }

        private static ScriptLine ParseLineForChar(int charIndex, string line, int lineNo, ActionEquivalenceProvider actionEquivProvider)
        {
            // Parse the line and find the action for the input charName. if not found, return null
            // Line example: 
            // <man> [Put] <COFFEE FILTER> (1) <COFFE MAKER> (1)

            if (string.IsNullOrEmpty(line) || line[0] != '<')
                return null;

            IList<Tuple<string, int>> paramList = new List<Tuple<string, int>>();
            IList<float> modifier = new List<float>();

            string pattAction = @"\[(\w+)\]";
            string pattParams = @"<([\w\s]+)>\s*\((\d+)\)\s*(:\d+:)?";
            string pattchar = @"<char(\d+)>";

            string[] sentences = line.Split('|');

            foreach (string sentence in sentences)
            {
                // Parse name
                Regex r = new Regex(pattchar);
                Match m = r.Match(sentence);

                if (!m.Success)
                {
                    throw new ScriptReaderException(string.Format("Can not parse character for the line containing {0}", sentence));
                }

                int parsedCharIndex = Int32.Parse(m.Groups[1].Value);
                if (parsedCharIndex != charIndex)
                    continue;


                //				Debug.Log (lineNo + ',' + sentence);



                // Parse action
                r = new Regex(pattAction);
                m = r.Match(sentence);

                if (!m.Success)
                    throw new ScriptReaderException(string.Format("Can not parse action for the line containing {0}", sentence));

                string actionStr = m.Groups[1].Value;

                // Parse parameters
                r = new Regex(pattParams);
                m = r.Match(sentence);

                while (m.Success)
                {
                    // <SOFA>, 1
                    paramList.Add(Tuple.Create(m.Groups[1].Value, int.Parse(m.Groups[2].Value)));
                    if (m.Groups.Count > 3)
                    {
                        int length = m.Groups[3].Value.Length;
                        if (length > 0)
                            modifier.Add(float.Parse(m.Groups[3].Value.Substring(1, length - 2)));
                    }
                    m = m.NextMatch();
                }

                if (paramList.Count == 1)
                {
                    string newActionStr;

                    if (actionEquivProvider.TryGetEquivalentAction(actionStr, paramList[0].Item1, out newActionStr))
                        actionStr = newActionStr;
                }
                else if (paramList.Count == 2)
                {
                    string newActionStr;

                    if (actionEquivProvider.TryGetEquivalentAction(actionStr, paramList[0].Item1, paramList[1].Item1, out newActionStr))
                        actionStr = newActionStr;
                }

                InteractionType action = (InteractionType)Enum.Parse(typeof(InteractionType), actionStr, true);
                return new ScriptLine() { Interaction = action, Parameters = paramList, LineNumber = lineNo, modifier = modifier };
            }

            // If the code reaches here, meaning no action for charName is found, and return null
            return null;

        }

    }

    public class ObjectNameSetSelector : IObjectSelector
    {
        ISet<string> objectNames;

        public ObjectNameSetSelector(IEnumerable<string> objectNames)
        {
            this.objectNames = new HashSet<string>(objectNames);
        }

        public bool IsSelectable(GameObject go)
        {
            return objectNames.Contains(go.name.ToLower());
        }
    }

    // Allows synonyms and forbidden strings
    public class SynonymSelector : IObjectSelector
    {
        private ISet<string> synonyms;

        public SynonymSelector(ISet<string> synonyms)
        {
            this.synonyms = synonyms;
        }

        // Hackish, replace with something more suitable
        public bool IsSelectable(GameObject go)
        {
            // var mKey = TimeMeasurement.Start("SelectObjects");

            List<string> path = go.GetPathNames();
            int occurrences = 0;
            bool lastMatches = false;

            // TimeMeasurement.Stop(mKey);

            for (int i = 0; i < path.Count; i++)
            {
                if (synonyms.Any(name => Utils.ObjectNameMatches(path[i], name)))
                {
                    occurrences++;
                    if (i == path.Count - 1)
                        lastMatches = true;
                }
            }
            return lastMatches && occurrences == 1;
        }

        public bool IsSelectable(string s)
        {
            return synonyms.Any(name => Utils.ObjectNameMatches(s, name));
        }

    }

    // Selects no objects
    public class EmptySelector : IObjectSelector
    {
        public static readonly EmptySelector Instance = new EmptySelector();

        public bool IsSelectable(GameObject go)
        {
            return false;
        }
    }

    public class FixedObjectSelector : IObjectSelector
    {
        private GameObject gameObject;

        public FixedObjectSelector(GameObject gameObject)
        {
            this.gameObject = gameObject;
        }

        public bool IsSelectable(GameObject go)
        {
            return go == gameObject;
        }
    }

    public class RoomSelector
    {
        private readonly static string[] RoomNames = { "bathroom", "kitchen", "livingroom", "bedroom" };

        NameEquivalenceProvider nameEqProvider;

        public RoomSelector(NameEquivalenceProvider nameEqProvider)
        {
            this.nameEqProvider = nameEqProvider;
        }

        // Example: living room -> livingroom, home office -> livingroom, kitchen -> kitchen, livingroom -> livingroom, ...
        public string CanonicalRoomName(string name)
        {
            name = ScriptUtils.TransformClassName(name);
            foreach (var canonicalRoom in RoomNames)
            {
                if (nameEqProvider.IsEquivalent(name, canonicalRoom))
                    return canonicalRoom;
            }
            return null;
        }

        // Example: living room, kitchen, home office -> true, PRE_FUR_kitchen_01 -> false
        public bool IsRoomName(string name)
        {
            return CanonicalRoomName(name) != null;
        }

        // Example: PRE_FUR_kitchen_01 -> kitchen, PRE_DEC_bottle_12 -> null
        public string ExtractRoomName(string objName)
        {
            foreach (var canonicalRoom in RoomNames)
            {
                if (Utils.ObjectNameMatches(objName, canonicalRoom))
                    return canonicalRoom;
            }
            return null;
        }

        public SynonymSelector GetRoomSelector(string name)
        {
            return new SynonymSelector(nameEqProvider.GetSynonyms(CanonicalRoomName(name)));
        }

    }

    static class ScriptObjectUtils
    {
        public static bool CanOpenOrClose(ScriptObjectData sod, InteractionType it)
        {
            if (sod.OpenStatus == OpenStatus.CLOSED && it == InteractionType.CLOSE ||
                    sod.OpenStatus == OpenStatus.OPEN && it == InteractionType.OPEN)
            {
                return false;
            }
            return true;
        }
    }

    static class Utils
    {
        // Example: (PRE_kitchen_01, kitchen) -> true, (PRE_FUR_kitchentable_01, kitchen) -> false
        public static bool ObjectNameMatches(string objName, string name)
        {
            char[] separators = { ' ', '_', '(', ')' };
            int index = 0;

            while (index < objName.Length && (index = objName.IndexOf(name, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                if ((index == 0 || separators.Contains(objName[index - 1])) &&
                        (index + name.Length >= objName.Length || separators.Contains(objName[index + name.Length])))
                    return true;
                index = index + name.Length;
            }
            return false;
        }

        public static void ParseNamePlaceName(string str, out string name, out string roomName)
        {
            Regex r = new Regex(@"\[([\w\s]+)\]");
            Match m = r.Match(str);

            if (m.Success)
            {
                roomName = m.Groups[1].Value.ToLower();
                name = str.Substring(0, m.Groups[0].Index).Trim();
            }
            else
            {
                roomName = null;
                name = str.Trim();
            }
        }

    }
}
