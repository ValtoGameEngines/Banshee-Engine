﻿//********************************** Banshee Engine (www.banshee3d.com) **************************************************//
//**************** Copyright (c) 2016 Marko Pintera (marko.pintera@gmail.com). All rights reserved. **********************//
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using bs;

namespace bs.Editor
{
    /** @addtogroup Utility-Editor
     *  @{
     */

    /// <summary>
    /// Handles undo & redo operations for changes made on game objects. Game objects can be recorded just before a change
    /// is made and the system will calculate the difference between that state and the state at the end of current frame.
    /// This difference will then be recorded as a undo/redo operation. The undo/redo operation will also take care of
    /// selecting the object & field it is acting upon.
    /// </summary>
    internal class GameObjectUndo
    {
        /// <summary>
        /// Contains information about a component that needs its diff recorded.
        /// </summary>
        private struct ComponentToRecord
        {
            private Component obj;
            private string path;
            private SerializedObject orgState;

            /// <summary>
            /// Creates a new object instance, recording the current state of the component.
            /// </summary>
            /// <param name="obj">Component to record the state of.</param>
            /// <param name="path">
            /// Path to the field which should be focused when performing the undo/redo operation. This should be the path
            /// as provided by <see cref="InspectableField"/>.
            /// </param>
            internal ComponentToRecord(Component obj, string path)
            {
                this.obj = obj;
                this.path = path;

                orgState = SerializedObject.Create(obj);
            }

            /// <summary>
            /// Generates the diff from the previously recorded state and the current state. If there is a difference
            /// an undo command is recorded.
            /// </summary>
            internal void RecordCommand()
            {
                if (obj.IsDestroyed)
                    return;

                SerializedDiff oldToNew = SerializedDiff.Create(orgState, obj);
                if (oldToNew.IsEmpty)
                    return;

                SerializedDiff newToOld = SerializedDiff.Create(obj, orgState);
                UndoRedo.Global.RegisterCommand(new RecordComponentUndo(obj, path, oldToNew, newToOld));
            }
        }

        /// <summary>
        /// Contains information about a scene object that needs its diff recorded. Note this will not record the entire
        /// scene object, but rather just its name, transform, active state and potentially other similar properties.
        /// It's components as well as hierarchy state are ignored.
        /// </summary>
        private struct SceneObjectHeaderToRecord
        {
            private SceneObject obj;
            private string path;
            private SceneObjectState orgState;

            /// <summary>
            /// Creates a new object instance, recording the current state of the scene object header.
            /// </summary>
            /// <param name="obj">Scene object to record the state of.</param>
            /// <param name="path">
            /// Path to the field which should be focused when performing the undo/redo operation.
            /// </param>
            internal SceneObjectHeaderToRecord(SceneObject obj, string path)
            {
                this.obj = obj;
                this.path = path;

                orgState = SceneObjectState.Create(obj);
            }

            /// <summary>
            /// Generates the diff from the previously recorded state and the current state. If there is a difference
            /// an undo command is recorded.
            /// </summary>
            internal void RecordCommand()
            {
                if (obj.IsDestroyed)
                    return;

                SceneObjectDiff oldToNew = SceneObjectDiff.Create(orgState, SceneObjectState.Create(obj));
                if (oldToNew.flags == 0)
                    return;

                SceneObjectDiff newToOld = SceneObjectDiff.Create(SceneObjectState.Create(obj), orgState);
                UndoRedo.Global.RegisterCommand(new RecordSceneObjectHeaderUndo(obj, path, oldToNew, newToOld));
            }
        }

        private static List<ComponentToRecord> components = new List<ComponentToRecord>();
        private static List<SceneObjectHeaderToRecord> sceneObjectHeaders = new List<SceneObjectHeaderToRecord>();

        /// <summary>
        /// Records the current state of the provided component, and generates a diff with the next state at the end of the
        /// frame. If change is detected an undo operation will be recorded. Generally you want to call this just before
        /// you are about to make a change to the component.
        /// </summary>
        /// <param name="obj">Component to record the state of.</param>
        /// <param name="fieldPath">
        /// Path to the field which should be focused when performing the undo/redo operation. This should be the path
        /// as provided by <see cref="InspectableField"/>.
        /// </param>
        public static void RecordComponent(Component obj, string fieldPath)
        {
            ComponentToRecord cmp = new ComponentToRecord(obj, fieldPath);
            components.Add(cmp);
        }

        /// <summary>
        /// Records the current state of the provided scene object header, and generates a diff with the next state at the
        /// end of the frame. If change is detected an undo operation will be recorded. Generally you want to call this
        /// just before you are about to make a change to the scene object header.
        ///
        /// Note this will not record the entire scene object, but rather just its name, transform, active state and
        /// potentially other similar properties. It's components as well as hierarchy state are ignored.
        /// </summary>
        /// <param name="obj">Scene object to record the state of.</param>
        /// <param name="fieldName">
        /// Name to the field which should be focused when performing the undo/redo operation.
        /// </param>
        public static void RecordSceneObjectHeader(SceneObject obj, string fieldName)
        {
            SceneObjectHeaderToRecord so = new SceneObjectHeaderToRecord(obj, fieldName);
            sceneObjectHeaders.Add(so);
        }

        /// <summary>
        /// Generates diffs for any objects that were previously recorded using any of the Record* methods. The diff is
        /// generated by comparing the state at the time Record* was called, compared to the current object state.
        /// </summary>
        public static void ResolveDiffs()
        {
            foreach (var entry in components)
                entry.RecordCommand();

            foreach (var entry in sceneObjectHeaders)
                entry.RecordCommand();

            components.Clear();
            sceneObjectHeaders.Clear();
        }
    }

    /// <summary>
    /// Contains information about scene object state, excluding information about its components and hierarchy.
    /// </summary>
    internal struct SceneObjectState
    {
        internal string name;
        internal Vector3 position;
        internal Quaternion rotation;
        internal Vector3 scale;
        internal bool active;

        /// <summary>
        /// Initializes the state from a scene object.
        /// </summary>
        /// <param name="so">Scene object to initialize the state from.</param>
        /// <returns>New state object.</returns>
        internal static SceneObjectState Create(SceneObject so)
        {
            SceneObjectState state = new SceneObjectState();
            state.name = so.Name;
            state.position = so.LocalPosition;
            state.rotation = so.LocalRotation;
            state.scale = so.LocalScale;
            state.active = so.Active;

            return state;
        }
    }

    /// <summary>
    /// Contains the difference between two <see cref="SceneObjectState"/> objects and allows the changes to be applied to
    /// a <see cref="SceneObject"/>. The value of the different fields is stored as its own state, while the flags field
    /// specified which of the properties is actually different.
    /// </summary>
    internal struct SceneObjectDiff
    {
        internal SceneObjectState state;
        internal SceneObjectDiffFlags flags;

        /// <summary>
        /// Creates a diff object storing the difference between two <see cref="SceneObjectState"/> objects.
        /// </summary>
        /// <param name="oldState">State of the scene object to compare from.</param>
        /// <param name="newState">State of the scene object to compare to.</param>
        /// <returns>Difference between the two scene object states.</returns>
        internal static SceneObjectDiff Create(SceneObjectState oldState, SceneObjectState newState)
        {
            SceneObjectDiff diff = new SceneObjectDiff();
            diff.state = new SceneObjectState();

            if (oldState.name != newState.name)
            {
                diff.state.name = newState.name;
                diff.flags |= SceneObjectDiffFlags.Name;
            }

            if (oldState.position != newState.position)
            {
                diff.state.position = newState.position;
                diff.flags |= SceneObjectDiffFlags.Position;
            }

            if (oldState.rotation != newState.rotation)
            {
                diff.state.rotation = newState.rotation;
                diff.flags |= SceneObjectDiffFlags.Rotation;
            }

            if (oldState.scale != newState.scale)
            {
                diff.state.scale = newState.scale;
                diff.flags |= SceneObjectDiffFlags.Scale;
            }

            if (oldState.active != newState.active)
            {
                diff.state.active = newState.active;
                diff.flags |= SceneObjectDiffFlags.Active;
            }

            return diff;
        }

        /// <summary>
        /// Applies the diff to an actual scene object.
        /// </summary>
        /// <param name="sceneObject">Scene object to apply the diff to.</param>
        internal void Apply(SceneObject sceneObject)
        {
            if (flags.HasFlag(SceneObjectDiffFlags.Name))
                sceneObject.Name = state.name;

            if (flags.HasFlag(SceneObjectDiffFlags.Position))
                sceneObject.LocalPosition = state.position;

            if (flags.HasFlag(SceneObjectDiffFlags.Rotation))
                sceneObject.LocalRotation = state.rotation;

            if (flags.HasFlag(SceneObjectDiffFlags.Scale))
                sceneObject.LocalScale = state.scale;

            if (flags.HasFlag(SceneObjectDiffFlags.Active))
                sceneObject.Active = state.active;
        }
    }

    /// <summary>
    /// Stores the field changes in a <see cref="SceneObject"/> as a difference between two states. Allows those changes to
    /// be reverted and re-applied. Does not record changes to scene object components or hierarchy, but just the fields
    /// considered its header (such as name, local transform and active state).
    /// </summary>
    [SerializeObject]
    internal class RecordSceneObjectHeaderUndo : UndoableCommand
    {
        private SceneObject obj;
        private string fieldPath;
        private SceneObjectDiff newToOld;
        private SceneObjectDiff oldToNew;

        /// <summary>
        /// Creates the new scene object undo command.
        /// </summary>
        /// <param name="obj">Scene object on which to apply the performed changes.</param>
        /// <param name="fieldPath">
        /// Optional path that controls which is the field being modified and should receive input focus when the command
        /// is executed. Note that the diffs applied have no restriction on how many fields they can modify at once, but
        /// only one field will receive focus.</param>
        /// <param name="oldToNew">
        /// Difference that can be applied to the old object in order to get the new object state.
        /// </param>
        /// <param name="newToOld">
        /// Difference that can be applied to the new object in order to get the old object state.
        /// </param>
        public RecordSceneObjectHeaderUndo(SceneObject obj, string fieldPath, SceneObjectDiff oldToNew, 
            SceneObjectDiff newToOld)
        {
            this.obj = obj;
            this.fieldPath = fieldPath;
            this.oldToNew = oldToNew;
            this.newToOld = newToOld;
        }

        /// <inheritdoc/>
        protected override void Commit()
        {
            if (obj == null)
                return;

            if (obj.IsDestroyed)
            {
                Debug.LogWarning("Attempting to commit state on a destroyed game-object.");
                return;
            }

            oldToNew.Apply(obj);
            FocusOnField();
        }

        /// <inheritdoc/>
        protected override void Revert()
        {
            if (obj == null)
                return;

            if (obj.IsDestroyed)
            {
                Debug.LogWarning("Attempting to revert state on a destroyed game-object.");
                return;
            }

            newToOld.Apply(obj);
            FocusOnField();
        }

        /// <summary>
        /// Selects the component's scene object and focuses on the specific field in the inspector, if the inspector
        /// window is open.
        /// </summary>
        private void FocusOnField()
        {
            if (obj != null)
            {
                if (Selection.SceneObject != obj)
                    Selection.SceneObject = obj;

                if (!string.IsNullOrEmpty(fieldPath))
                {
                    InspectorWindow inspectorWindow = EditorWindow.GetWindow<InspectorWindow>();
                    inspectorWindow?.FocusOnField(obj.UUID, fieldPath);
                }
            }
        }
    }


    /// <summary>
    /// Stores the field changes in a <see cref="Component"/> as a difference between two states. Allows those changes to
    /// be reverted and re-applied. 
    /// </summary>
    [SerializeObject]
    internal class RecordComponentUndo : UndoableCommand
    {
        private Component obj;
        private string fieldPath;
        private SerializedDiff newToOld;
        private SerializedDiff oldToNew;

        /// <summary>
        /// Creates the new component undo command.
        /// </summary>
        /// <param name="obj">Component on which to apply the performed changes.</param>
        /// <param name="fieldPath">
        /// Optional path that controls which is the field being modified and should receive input focus when the command
        /// is executed. Note that the diffs applied have no restriction on how many fields they can modify at once, but
        /// only one field will receive focus.</param>
        /// <param name="oldToNew">
        /// Difference that can be applied to the old object in order to get the new object state.
        /// </param>
        /// <param name="newToOld">
        /// Difference that can be applied to the new object in order to get the old object state.
        /// </param>
        public RecordComponentUndo(Component obj, string fieldPath, SerializedDiff oldToNew, SerializedDiff newToOld)
        {
            this.obj = obj;
            this.fieldPath = fieldPath;
            this.oldToNew = oldToNew;
            this.newToOld = newToOld;
        }

        /// <inheritdoc/>
        protected override void Commit()
        {
            if (oldToNew == null || obj == null)
                return;

            if (obj.IsDestroyed)
            {
                Debug.LogWarning("Attempting to commit state on a destroyed game-object.");
                return;
            }

            oldToNew.Apply(obj);
            FocusOnField();
        }

        /// <inheritdoc/>
        protected override void Revert()
        {
            if (newToOld == null || obj == null)
                return;

            if (obj.IsDestroyed)
            {
                Debug.LogWarning("Attempting to revert state on a destroyed game-object.");
                return;
            }

            newToOld.Apply(obj);
            FocusOnField();
        }

        /// <summary>
        /// Selects the component's scene object and focuses on the specific field in the inspector, if the inspector
        /// window is open.
        /// </summary>
        private void FocusOnField()
        {
            SceneObject so = obj.SceneObject;
            if (so != null)
            {
                if (Selection.SceneObject != so)
                    Selection.SceneObject = so;

                if (!string.IsNullOrEmpty(fieldPath))
                {
                    InspectorWindow inspectorWindow = EditorWindow.GetWindow<InspectorWindow>();
                    inspectorWindow?.FocusOnField(obj.UUID, fieldPath);
                }
            }
        }
    }

    /** @} */
}
