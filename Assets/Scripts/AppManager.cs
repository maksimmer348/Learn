using App.UI;
using UnityEngine;

namespace App
{
    public class AppManager : MonoBehaviour
    {
        void Start()
        {
            LeanTween.reset();
            LeanTween.init();
            
            WindowsManager.Instance.CreateWindow<MainViewController>(new MainViewStarter()).Show();
        }

        private void OnDestroy()
        {
            
        }
    }
}