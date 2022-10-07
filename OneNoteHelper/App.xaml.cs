using System.Windows;
using CommonWpf.Classes;

namespace OneNoteHelper
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		public App()
		{
			AppInitializer.Initialize();
		}
	}
}
