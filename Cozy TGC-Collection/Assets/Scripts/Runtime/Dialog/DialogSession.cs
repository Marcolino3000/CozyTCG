using System;
using System.Collections;
using Core;
using Tree;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace CozyTGC
{
    /// <summary>
    /// Starts and stops dialogs. This is the seam the rest of the game talks to: call
    /// <see cref="Play(DialogTree)"/> with a tree, listen to <see cref="Finished"/> when
    /// the conversation is over.
    ///
    /// Implements the two package-side hand-over interfaces rather than poking at
    /// <see cref="DialogTreeRunner"/> directly, so the runner keeps ownership of its own
    /// state: <see cref="IDialogTreeSetter"/> swaps the tree and registers the completion
    /// callback, <see cref="IDialogStarter"/> starts and resets the run.
    /// </summary>
    public class DialogSession : MonoBehaviour, IDialogStarter, IDialogTreeSetter
    {
        public event Action OnStartDialog;
        public event Action OnStopDialog;
        public event Action<DialogTree, Action<bool>> OnSetDialogTree;

        /// <summary>
        /// True when the tree ran to its end, false when it was stopped early.
        /// </summary>
        public event Action<bool> Finished;

        [SerializeField] DialogTree tree;
        [SerializeField] bool playOnStart = true;

        public bool IsPlaying { get; private set; }

        void Awake()
        {
            EnsureEventSystem();
        }

        IEnumerator Start()
        {
            if (!playOnStart) yield break;

            // DialogBuilderHQ collects its clients in its own Start and there is no
            // execution order between the two, so handing a tree over in the same frame
            // can reach a runner that has no receivers yet. One frame is enough.
            yield return null;
            Play(tree);
        }

        [ContextMenu("Play")]
        public void Play()
        {
            Play(tree);
        }

        public void Play(DialogTree dialogTree)
        {
            if (dialogTree == null)
            {
                Debug.LogWarning("[Cozy TGC] DialogSession has no dialog tree to play.", this);
                return;
            }

            if (OnSetDialogTree == null)
            {
                Debug.LogWarning("[Cozy TGC] No DialogTreeRunner picked this session up. " +
                                 "A DialogBuilderHQ with its Tree Runner assigned has to be " +
                                 "in the scene.", this);
                return;
            }

            tree = dialogTree;
            IsPlaying = true;
            OnSetDialogTree.Invoke(dialogTree, HandleFinished);
            OnStartDialog?.Invoke();
        }

        [ContextMenu("Stop")]
        public void Stop()
        {
            if (!IsPlaying) return;
            OnStopDialog?.Invoke();
        }

        void HandleFinished(bool completed)
        {
            IsPlaying = false;
            Finished?.Invoke(completed);
        }

        /// <summary>
        /// DialogBuilderHQ adds a <c>StandaloneInputModule</c> when it finds no event
        /// system, and that module reads the legacy Input class - which throws outright
        /// while Active Input Handling is set to the Input System package. Getting there
        /// first with a module that matches the project's input backend keeps clicks on
        /// the options working in a scene that has no UI of its own.
        /// </summary>
        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

#if UNITY_EDITOR
        public void EditorBind(DialogTree dialogTree, bool autoPlay)
        {
            tree = dialogTree;
            playOnStart = autoPlay;
        }
#endif
    }
}
