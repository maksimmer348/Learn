namespace App.UI
{
    public class MainViewStarter : IWindowStarter
    {
        public string GetGroup() => "Main";
        public string GetName() => "MainView";

        public void SetupModels(ViewController viewController)
        {
            
        }
    }
    
    
}