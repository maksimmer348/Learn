using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class WindowsManager : MonoBehaviour
{
    public class WindowPrefab
    {
        protected ViewController prefab;
        public ViewController Prefab => prefab;

        protected Dictionary<int, ViewController> windows;

        public WindowPrefab(ViewController prefab)
        {
            this.prefab = prefab;
            windows = new Dictionary<int, ViewController>();
        }

        public void AddWnd(ViewController wnd)
        {
            int id = -1;
            while (windows.ContainsKey(++id))
            {
            }

            wnd.ID = id;
            windows.Add(wnd.ID, wnd);
        }

        public ViewController GetWnd(int id)
        {
            if (!windows.ContainsKey(id))
                return null;

            return windows[id];
        }

        public List<ViewController> GetWnds(bool onlyActive)
        {
            var wnds = new List<ViewController>();
            foreach (var wnd in windows)
                if (wnd.Value != null)
                    if (onlyActive && wnd.Value.gameObject.activeInHierarchy || !onlyActive)
                        wnds.Add(wnd.Value);
            return wnds;
        }

        public bool HaveWindows(bool includeInactive = false)
        {
            if (includeInactive)
                return windows.Count != 0;

            int activeCount = 0;
            foreach (ViewController wnd in windows.Values)
                if (wnd.gameObject.activeSelf)
                    activeCount++;

            return activeCount > 0;
        }

        public void RemoveWnd(ViewController wnd)
        {
            foreach (var item in windows)
                if (item.Key == wnd.ID)
                {
                    windows.Remove(item.Key);
                    break;
                }
        }

        public void RemoveWnd(int id)
        {
            windows.Remove(id);
        }

        public ViewController GetFirstNonActive()
        {
            foreach (ViewController wnd in windows.Values)
                if (!wnd.gameObject.activeSelf && wnd.IsPreloaded)
                    return wnd;
            return null;
        }

        public void CleanUp()
        {
            var keys = new List<int>();
            foreach (var wnd in windows)
                if (wnd.Value == null)
                    keys.Add(wnd.Key);

            foreach (int t in keys)
                windows.Remove(t);
        }
    }

    public static float WindowShowHideTime = 0.2f;

    public static event Action<ViewController> OnWindowCreated;
    public static event Action<ViewController> OnWindowClosed;

    protected Dictionary<string, WindowPrefab> loadedPrefabs = new Dictionary<string, WindowPrefab>();

    public Dictionary<string, WindowPrefab> LoadedPrefabs => loadedPrefabs;

    protected List<ViewController> visibleViews = new List<ViewController>();

    public List<ViewController> VisibleViews => visibleViews;

    public Canvas UIRoot => GetComponent<Canvas>();

    public Camera UICamera => GetComponentInParent<Camera>();

    protected static WindowsManager instance;

    public static WindowsManager Instance
    {
        get
        {
            if (instance == null)
            {
                var instObj = Instantiate(Resources.Load<GameObject>("UI/UI"));
                instObj.name = "UI";
                instance = instObj.GetComponentInChildren<WindowsManager>();
            }

            return instance;
        }
    }

    private void OnDestroy()
    {
        instance = null;
    }

    public static string GetFullWindowTypeName(IWindowStarter info)
    {
        return info.GetGroup() + "_" + info.GetName();
    }

    public string GetWindowPrefabPath(IWindowStarter info)
    {
        return "UI/" + info.GetGroup() + "/" + info.GetName();
    }

    private ViewController InstantiateView(ViewController prefab)
    {
        ViewController wnd = Instantiate(prefab, UIRoot.transform, false);
        wnd.name = wnd.name.Replace("(Clone)", "");
        wnd.transform.localScale = Vector3.one;

        return wnd;
    }
    

    public ViewController LoadWindowPrefab(IWindowStarter starter)
    {
        var go = Resources.Load<GameObject>(GetWindowPrefabPath(starter));
        return go.GetComponent<ViewController>();
    }

    public ViewController CreateWindow(ViewController parent, IWindowStarter starter)
    {
        return CreateWindow<ViewController>(parent, starter);
    }

    public ViewController CreateWindow(IWindowStarter starter)
    {
        return CreateWindow<ViewController>(null, starter);
    }

    public T CreateWindow<T>(IWindowStarter starter) where T : ViewController
    {
        return CreateWindow<T>(null, starter);
    }

    public T CreateWindow<T>(ViewController parent, IWindowStarter starter) where T : ViewController
    {
        WindowPrefab wndPrefab = null;
        string windowTypeName = GetFullWindowTypeName(starter);

        if (LoadedPrefabs.ContainsKey(windowTypeName))
            wndPrefab = LoadedPrefabs[windowTypeName];

        if (wndPrefab == null)
        {
            ViewController wndPrefabObj = LoadWindowPrefab(starter);
            if (wndPrefabObj != null)
            {
                wndPrefab = new WindowPrefab(wndPrefabObj);
                loadedPrefabs.Add(windowTypeName, wndPrefab);
            }
            else
            {
                return null;
            }
        }

        if (wndPrefab.Prefab.OnlyOneCanBeVisible)
        {
            RefreshVisibleViews();
            var visibleView = VisibleViews.FirstOrDefault(e => e.TypeInfo.GetGroup() == starter.GetGroup() && e.TypeInfo.GetName() == starter.GetName());
            if (visibleView != null)
            {
                visibleView.Close();
            }
        }

        ViewController controller;

        ViewController cached = wndPrefab.GetFirstNonActive();
        if (cached != null)
        {
            controller = cached;
            controller.IsPreloaded = false;
            controller.gameObject.SetActive(true);
        }
        else
        {
            controller = InstantiateView(wndPrefab.Prefab);
            wndPrefab.AddWnd(controller);
        }

        if (controller != null)
        {
            starter.SetupModels(controller);
            controller.Parent = parent;

            try
            {
                controller.Init(starter);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to init view controller for starter (" + GetFullWindowTypeName(starter) + "):" +
                               e.Message + "\n" + e.StackTrace);
#if UNITY_EDITOR
                throw;
#endif
            }

            controller.gameObject.SetActive(false);

            controller.OnDepthChanged -= OnWindowDepthChangedInternal;
            controller.OnDepthChanged += OnWindowDepthChangedInternal;

            controller.OnWindowClosed -= OnWindowClosedInternal;
            controller.OnWindowClosed += OnWindowClosedInternal;

            controller.OnWindowDestroyed -= OnWindowDestroyedInternal;
            controller.OnWindowDestroyed += OnWindowDestroyedInternal;

            OnWindowCreated?.Invoke(controller);
            
            return controller as T;
        }


        return null;
    }

    private void OnWindowDestroyedInternal(ViewController obj)
    {
        RemoveWindow(obj);
    }

    private void OnWindowClosedInternal(ViewController obj)
    {
        OnWindowClosedInternal(obj, true);
    }

    private void OnWindowClosedInternal(ViewController obj, bool fireCallback)
    {
        if (!obj.DestroyOnClose)
            obj.IsPreloaded = true;

        visibleViews.RemoveAll((v) => v == obj);
        SortWindows();

        if (fireCallback)
            OnWindowClosed?.Invoke(obj);
    }

    private void OnWindowDepthChangedInternal(ViewController obj)
    {
        if (!visibleViews.Contains(obj))
            visibleViews.Add(obj);

        SortWindows();
    }

    private static int CompareViews(ViewController view1, ViewController view2)
    {
        return view2.Depth.CompareTo(view1.Depth);
    }

    protected void SortWindows()
    {
        visibleViews.Sort(CompareViews);
    }

    protected void RefreshVisibleViews()
    {
        visibleViews.Clear();

        foreach (var wnd in LoadedPrefabs)
        {
            var wnds = wnd.Value.GetWnds(true);
            foreach (ViewController t in wnds)
                if (t.State == ViewController.WindowState.Showing || t.State == ViewController.WindowState.Visible)
                    visibleViews.Add(t);
        }

        SortWindows();
    }

    public ViewController GetWindow(string typeName, int id)
    {
        if (LoadedPrefabs.ContainsKey(typeName))
            return LoadedPrefabs[typeName].GetWnd(id);

        return null;
    }

    public ViewController GetWindow(string uniqueID)
    {
        string[] parts = uniqueID.Split('!');
        if (parts.Length != 2)
            return null;

        int id = int.Parse(parts[1]);

        return GetWindow(parts[0], id);
    }

    public List<ViewController> GetAllWindows(string typeName, bool onlyActive)
    {
        if (LoadedPrefabs.ContainsKey(typeName))
            return LoadedPrefabs[typeName].GetWnds(onlyActive);

        return new List<ViewController>();
    }

    public bool HaveWindows(string typeName)
    {
        if (LoadedPrefabs.ContainsKey(typeName))
            return LoadedPrefabs[typeName].HaveWindows();
        return false;
    }

    public List<ViewController> GetAllWindows()
    {
        var Windows = new List<ViewController>();
        foreach (var wnd in LoadedPrefabs)
        {
            var vw = wnd.Value.GetWnds(false);
            foreach (ViewController vc in vw)
                if (Windows.Contains(vc) == false)
                    Windows.Add(vc);
        }

        return Windows;
    }

    private void RemoveWindow(ViewController wnd)
    {
        string key = GetFullWindowTypeName(wnd.TypeInfo);

        OnWindowClosedInternal(wnd, false);

        if (LoadedPrefabs.ContainsKey(key))
        {
            WindowPrefab prefab = LoadedPrefabs[key];
            prefab.RemoveWnd(wnd);
            if (!prefab.HaveWindows())
                LoadedPrefabs.Remove(key);
        }
    }

    private void ClearDestroyedWindows()
    {
        foreach (var wnd in LoadedPrefabs)
            wnd.Value.CleanUp();
    }
}