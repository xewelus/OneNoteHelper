using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using Common;
using CommonWpf;
using CommonWpf.Classes.UI;

namespace TestOneNote
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			this.InitializeComponent();
		}

		private void DumpClipboard_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				IDataObject dataObject = Clipboard.GetDataObject();
				if (dataObject == null) return;

				string[] formats = dataObject.GetFormats().ToArray();

				Dictionary<string, object> dic = formats.ToDictionary(f => f, f => dataObject.GetData(f));
				string folder = FS.GetProjectPath("outputs", DateTime.Now.ToString("yyMMdd HHmmss"));
				this.lastFolder = folder;
				formats.ForEach(f => DumpData(dataObject: dataObject, f: f, folder: folder));

				UIHelper.ShowMessage($"Processed formats: {formats.Length}");
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}

		private string lastFolder;
		private static void DumpData(IDataObject dataObject, string f, string folder)
		{
			object o = dataObject.GetData(f);
			string ext = ".log";
			byte[] content = null;
			if (o is MemoryStream ms)
			{
				byte[] bytes = ms.ToArray();
				o = $"MemoryStream\r\n{BitConverter.ToString(bytes).Replace("-", " ")}";
				ms.Position = 0;
			}
			else if (o is Bitmap bitmap)
			{
				using (MemoryStream stream = new MemoryStream())
				{
					bitmap.Save(stream, ImageFormat.Png);
					content = stream.ToArray();
					ext = ".png";
				}
			}
			else if (o is Image image)
			{
				using (MemoryStream stream = new MemoryStream())
				{
					image.Save(stream, ImageFormat.Png);
					content = stream.ToArray();
					ext = ".png";
				}
			}

			string filename = f + ext;
			Path.GetInvalidFileNameChars().ForEach(c => filename = filename.Replace(c.ToString(), ((int)c).ToString()));
			string file = FS.Combine(folder, filename);
			FS.EnsureFileFolder(file);

			if (content == null)
			{
				File.WriteAllText(file, o?.ToString() ?? "");
			}
			else
			{
				File.WriteAllBytes(file, content);
			}
		}

		private void OpenDumpFolder_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				if (this.lastFolder == null)
				{
					FS.OpenInDefaultApp(FS.GetProjectPath("outputs"));
				}
				else
				{
					FS.OpenInDefaultApp(this.lastFolder);
				}
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}
	}
}
