using System.Windows;
using CommonWpf.Classes.UI;

namespace TestOneNote
{
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application
	{
		public App()
		{
			ExceptionHandler.Init();
		}
	}
}
