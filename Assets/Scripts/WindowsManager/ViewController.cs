using UnityEngine;
using UnityEngine.UI;

// ReSharper disable ForCanBeConvertedToForeach

[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(GraphicRaycaster))]
[RequireComponent(typeof(CanvasGroup))]
public class ViewController : MonoBehaviour
{
    public enum WindowState
    {
        None,
        Hidden,
        Showing,
        Visible,
        Hiding
    };

    protected class GOState
    {
        public bool isRoot;
        public GameObject obj;
        public Transform parent;
        public bool active;
        public GOState[] childrens;

        public GOState(GameObject root, bool isRoot)
        {
            this.isRoot = isRoot;
            Init(root);
        }

        public void Init(GameObject root)
        {
            obj = root;
            parent = obj.transform.parent;
            active = obj.activeSelf;
            childrens = new GOState[obj.transform.childCount];
            for (int i = 0; i < childrens.Length; i++)
                childrens[i] = new GOState(obj.transform.GetChild(i).gameObject, false);
        }

        public GOState GetChild(Transform trans)
        {
            for (int i = 0; i < childrens.Length; i++)
                if (childrens[i].obj == trans.gameObject)
                    return childrens[i];
            return null;
        }

        public void Restore()
        {
            if (obj == null)
                return;

            if (!isRoot)
                obj.SetActive(active);

            for (int i = 0; i < childrens.Length; i++)
                childrens[i].RestoreParents();

            for (int i = 0; i < obj.transform.childCount; i++)
            {
                Transform child = obj.transform.GetChild(i);
                if (GetChild(child) == null)
                    Destroy(child.gameObject);
            }

            for (int i = 0; i < childrens.Length; i++)
                childrens[i].Restore();
        }

        protected void RestoreParents()
        {
            if (obj == null || parent == null)
                return;

            obj.transform.SetParent(parent, false);
            for (int i = 0; i < childrens.Length; i++)
                childrens[i].RestoreParents();
        }
    };

    protected static GameObject inputBlockerPrefab;
    protected static GameObject windowBackgroundPrefab;

    public event System.Action<ViewController> OnShowWindow;
    public event System.Action<ViewController> OnWindowWasShown;
    public event System.Action<ViewController> OnHideWindow;
    public event System.Action<ViewController> OnWindowClosed;
    public event System.Action<ViewController> OnActualSizeInit;
    public event System.Action<ViewController> OnDepthChanged;
    public event System.Action<ViewController> OnWindowDestroyed;

    public Vector3 Position
    {
        get => transform.localPosition;
        set => transform.localPosition = value;
    }

    [System.NonSerialized] public int ID;

    [System.NonSerialized] public ViewController Parent = null;

    [System.NonSerialized] public bool IsPreloaded = false;

    public bool AnimatedShow = true;
    public bool AnimatedClose = true;

    public bool InputBlockNeeded = true;

    public bool ShowBackground = true;
    public bool CloseByTapOnBackground = true;

    public bool DestroyOnClose = true;

    public bool CloseAllChainOnClose;
    public bool OnlyOneCanBeVisible = false;

    protected bool ignoreTimeScale = false;

    protected WindowState state = WindowState.None;

    protected bool immediateShowing;

    protected IWindowStarter typeInfo;
    public IWindowStarter TypeInfo => typeInfo;

    public WindowState State => state;

    public string UniqueID => WindowsManager.GetFullWindowTypeName(typeInfo) + "!" + ID.ToString();

    protected Canvas cachedPanel;

    public Canvas CachedPanel => cachedPanel;

    protected CanvasGroup canvasGroup;

    protected RectTransform rectTransform;

    protected RectTransform background;

    protected GOState rootState;

    public bool restoreHierarchyState = true;

    protected bool deinitialized = true;

    [System.NonSerialized] public float ShowTime = WindowsManager.WindowShowHideTime;

    public int Depth
    {
        get => rectTransform.GetSiblingIndex();
        set
        {
            if (rectTransform.GetSiblingIndex() != value)
            {
                rectTransform.SetSiblingIndex(value);
                OnDepthChanged?.Invoke(this);
            }
        }
    }

    protected static readonly string WndBackgroundPrefabPath = "UI/WindowBackground";
    protected static readonly string InputBlockerPrefabPath = "UI/WindowInputBlocker";

    protected static void PreloadResources()
    {
        if (windowBackgroundPrefab == null)
        {
            var async = Resources.Load<GameObject>(WndBackgroundPrefabPath);
            windowBackgroundPrefab = async;
        }

        if (inputBlockerPrefab == null)
        {
            var async = Resources.Load<GameObject>(InputBlockerPrefabPath);
            inputBlockerPrefab = async;
        }
    }

    protected virtual void Awake()
    {
        cachedPanel = GetComponent<Canvas>();
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();

        // UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
    //     UnityEngine.SceneManagement.LoadSceneMode loadSceneMode)
    // {
    //     DestroyWindow(true);
    // }

    public virtual void Init(IWindowStarter starter)
    {
        typeInfo = starter;

        PreloadResources();

        if (InputBlockNeeded && background == null)
        {
            GameObject back = null;
            if (ShowBackground)
            {
                if (windowBackgroundPrefab != null)
                    back = Instantiate(windowBackgroundPrefab);
            }
            else
            {
                if (inputBlockerPrefab != null)
                    back = Instantiate(inputBlockerPrefab);
            }

            if (back != null)
            {
                back.name = "WindowBackground";
                back.transform.SetParent(transform, false);
                back.transform.localScale = Vector3.one;
                back.transform.localPosition = Vector3.zero;
                back.GetComponent<RectTransform>().SetAsFirstSibling();

                UnityEngine.EventSystems.EventTrigger eventTrigger =
                    back.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (eventTrigger == null)
                    eventTrigger = back.AddComponent<UnityEngine.EventSystems.EventTrigger>();

                var clickCallback = new UnityEngine.EventSystems.EventTrigger.Entry()
                    { eventID = UnityEngine.EventSystems.EventTriggerType.PointerClick };
                clickCallback.callback.AddListener(OnBackgroundClickSink);
                eventTrigger.triggers.Add(clickCallback);

                background = back.GetComponent<RectTransform>();
            }
        }

        if (!DestroyOnClose && restoreHierarchyState && rootState == null)
            rootState = new GOState(gameObject, true);

        deinitialized = false;
    }

    public virtual void Show()
    {
        if (state == WindowState.Showing || state == WindowState.Visible)
            return;

        state = WindowState.Showing;

        gameObject.SetActive(true);

        BringToTop();

        OnShowWindow?.Invoke(this);

        if (AnimatedShow)
            StartShowAnimation();
        else
            LeanTween.delayedCall(gameObject, OnShownCoroutine, 1);
    }

    public virtual void ShowImmediate()
    {
        bool prevAS = AnimatedShow;
        AnimatedShow = false;

        immediateShowing = true;
        Show();

        AnimatedShow = prevAS;
    }

    public void BringToTop()
    {
        int depth = Depth;
        rectTransform.SetAsLastSibling();
        if (depth != Depth)
            OnDepthChanged?.Invoke(this);
    }

    private void FireOnActualSizeInit()
    {
        OnActualSizeInit?.Invoke(this);

        if (AnimatedShow)
            DisableInteractables();
    }

    private void OnShownCoroutine()
    {
        FireOnActualSizeInit();

        OnShown();
    }

    protected virtual void StartShowAnimation()
    {
        SetWindowInvisible();

        LeanTween.delayedCall(gameObject, StartShowAnimationCoroutine, 1);
    }

    private float srcAlpha = 1.0f;

    protected virtual void SetWindowInvisible()
    {
        if (canvasGroup != null)
        {
            srcAlpha = canvasGroup.alpha;
            canvasGroup.alpha = 0.01f;
        }
    }

    protected virtual void SetWindowVisible()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = srcAlpha;
    }

    private void StartShowAnimationCoroutine()
    {
        FireOnActualSizeInit();

        SetWindowVisible();

        ShowAnimation();
    }

    //Animation methods. Override to change show animation behaviour. OnShown CALLBACK SHOULD BE CALLED ON COMPLETE
    protected virtual void ShowAnimation()
    {
        //AnimateScaleShow(gameObject, OnShown);

        AnimateAlpha(0.01f, 1.0f, OnShown);
    }

    //Animation methods. Override to change hide animation behaviour. OnHidden CALLBACK SHOULD BE CALLED ON COMPLETE
    protected virtual void HideAnimation()
    {
        // AnimateScaleHide(gameObject, OnHidden);

        AnimateAlpha(1.0f, 0.01f, OnHidden);
    }

    protected virtual void StartHideAnimation()
    {
        HideAnimation();
    }

    protected virtual void AnimateScaleShow(GameObject root, System.Action callback)
    {
        root.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        LeanTween.scale(root, Vector3.one, ShowTime).setEase(LeanTweenType.easeOutBack).setOnComplete(callback)
            .setUseEstimatedTime(ignoreTimeScale);
    }

    protected virtual void AnimateScaleHide(GameObject root, System.Action callback)
    {
        LeanTween.scale(root, new Vector3(0.5f, 0.5f, 0.5f), ShowTime).setEase(LeanTweenType.easeInBack)
            .setOnComplete(callback).setUseEstimatedTime(ignoreTimeScale);
    }

    protected void AnimateAlpha(float from, float to, System.Action callback = null)
    {
        if (cachedPanel != null)
        {
            canvasGroup.alpha = from;
            LeanTween.alphaCanvas(canvasGroup, to, ShowTime).setUseEstimatedTime(ignoreTimeScale)
                .setOnComplete(callback);
        }
    }

    protected void AnimateAlpha(CanvasGroup widget, float from, float to, System.Action callback = null)
    {
        if (widget != null)
        {
            widget.alpha = from;
            LeanTween.alphaCanvas(widget, to, ShowTime).setUseEstimatedTime(ignoreTimeScale).setOnComplete(callback);
        }
    }

    protected virtual void OnShown()
    {
        state = WindowState.Visible;

        if (AnimatedShow) EnableColliders();

        OnWindowWasShown?.Invoke(this);

        immediateShowing = false;
    }

    protected virtual void OnHidden()
    {
        state = WindowState.Hidden;

        OnHiddenClose();
    }

    private void OnHiddenClose()
    {
        OnWindowClosed?.Invoke(this);

        if (DestroyOnClose)
        {
            DestroyWindow();
        }
        else
        {
            if (restoreHierarchyState)
                rootState?.Restore();
            gameObject.SetActive(false);
        }

        if (!deinitialized)
            DeInit(false);
    }

    protected virtual void DeInit(bool fromDestroy)
    {
        deinitialized = true;
    }

    public virtual void Close()
    {
        if (CloseAllChainOnClose)
            CloseAllChain();
        else
            CloseSingle();
    }

    protected virtual void CloseSingle()
    {
        if (state == WindowState.Hiding || state == WindowState.Hidden)
            return;

        state = WindowState.Hiding;

        OnHideWindow?.Invoke(this);

        if (AnimatedClose)
        {
            DisableInteractables();
            StartHideAnimation();
        }
        else
        {
            OnHidden();
        }
    }

    protected void OnBackgroundClickSink(UnityEngine.EventSystems.BaseEventData baseEventData)
    {
        if (state == WindowState.Visible && CloseByTapOnBackground)
            OnBackgroundClick();
    }

    public virtual void OnBackgroundClick()
    {
        Close();
    }

    public void CloseAllChain()
    {
        CloseSingle();
        if (Parent != null)
            Parent.CloseAllChain();
    }

    protected System.Collections.Generic.List<CanvasGroup> disabledInteractables =
        new System.Collections.Generic.List<CanvasGroup>();

    protected virtual void DisableInteractables()
    {
        disabledInteractables.Clear();
        var colliders = GetComponentsInChildren<CanvasGroup>();
        for (int i = 0; i < colliders.Length; ++i)
            //if (colliders[i].interactable)
        {
            if (InputBlockNeeded && background != null && colliders[i].gameObject == background.gameObject &&
                colliders[i].gameObject == canvasGroup.gameObject)
                continue;

            colliders[i].interactable = false;
            disabledInteractables.Add(colliders[i]);
        }
    }

    protected virtual void EnableColliders()
    {
        for (int i = 0; i < disabledInteractables.Count; i++)
            if (disabledInteractables[i] != null)
                disabledInteractables[i].interactable = true;
        disabledInteractables.Clear();
    }

    public void DestroyWindow(bool immediate = false)
    {
        OnWindowDestroyed?.Invoke(this);

        if (immediate)
            Destroy(gameObject);
        else
            Destroy(gameObject);
    }

    protected virtual void OnDestroy()
    {
        if (!deinitialized)
            DeInit(true);

        // UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        
        OnShowWindow = null;
        OnWindowWasShown = null;
        OnHideWindow = null;
        OnDepthChanged = null;
        OnWindowClosed = null;
        OnActualSizeInit = null;
        OnWindowDestroyed = null;
    }
}