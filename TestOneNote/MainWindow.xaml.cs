using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

		private static Regex dateRegex = new Regex("^(\\d+) ([а-€]+) (\\d+) г.$");
		private static Regex timeRegex = new Regex("^(\\d+):(\\d+)$");

		private void ProcessPages_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				string text = Clipboard.GetText();
				if (string.IsNullOrEmpty(text))
				{
					UIHelper.ShowWarning("Clipboard is empty.");
					return;
				}

				if (!Clipboard.ContainsData("OneNote 2010 Internal"))
				{
					UIHelper.ShowWarning("No OneNote data in clipboard.");
					return;
				}

				text = text.Replace("\r\n", "\n");
				string[] lines = text.Split('\n');
				Dictionary<DateTime, StringBuilder> records = GetRecords(lines);

				Dictionary<DateTime, string> records2 = new Dictionary<DateTime, string>();
				foreach (KeyValuePair<DateTime, StringBuilder> pair in records)
				{
					string str = pair.Value.ToString();
					str = str.Trim('\r', '\n');
					records2[pair.Key] = str;
				}


				records2 = records2;
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}

		private static Dictionary<DateTime, StringBuilder> GetRecords(string[] lines)
		{
			Dictionary<DateTime, StringBuilder> records = new Dictionary<DateTime, StringBuilder>();
			int lineNum = 0;
			StringBuilder sb = null;
			LinesContext context = LinesContext.NeedDate;
			DateTime? date = null;
			int emptyCount = 0;
			foreach (string line in lines)
			{
				if (string.IsNullOrEmpty(line))
				{
					emptyCount++;
				}
				else
				{
					emptyCount = 0;
				}

				lineNum++;
				try
				{
					if (context == LinesContext.NeedDate)
					{
						if (string.IsNullOrEmpty(line)) continue;

						Match match = dateRegex.Match(line);
						if (!match.Success)
						{
							throw new Exception("Date not found.");
						}

						date = GetDay(match);

						context = LinesContext.NeedTime;
					}
					else if (context == LinesContext.NeedTime)
					{
						if (date == null) throw new NullReferenceException(nameof(date));

						Match match = timeRegex.Match(line);
						if (!match.Success)
						{
							throw new Exception("Time not found.");
						}

						TimeSpan time = GetTime(match);
						date = date.Value.Add(time);

						sb = records.GetOrCreate(date.Value);
						context = LinesContext.NeedText;
					}
					else if (context == LinesContext.NeedText)
					{
						if (sb == null) throw new NullReferenceException(nameof(sb));

						if (sb.Length > 0) sb.AppendLine();

						sb.Append(line);
						if (emptyCount > 3)
						{
							context = LinesContext.NeedDate;
							sb = null;
						}
					}
				}
				catch (Exception ex)
				{
					throw new Exception($"Error while processing line #{lineNum}: {line}", ex);
				}
			}
			return records;
		}

		private static DateTime GetDay(Match match)
		{
			string s1 = match.Groups[1].Value;
			int day;

			try
			{
				day = int.Parse(s1);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing day '{s1}'.", ex);
			}

			string s2 = match.Groups[2].Value;

			int month;
			try
			{
				month = GetMonth(s2);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing month '{s2}'.", ex);
			}

			string s3 = match.Groups[3].Value;
			int year;
			try
			{
				year = int.Parse(s3);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing year '{s3}'.", ex);
			}

			return new DateTime(year, month, day);
		}

		private static readonly string[] months = 
		{
			"€нвар€",
			"феврал€",
			"марта",
			"апрел€",
			"ма€",
			"июн€",
			"июл€",
			"августа",
			"сент€бр€",
			"окт€бр€",
			"но€бр€",
			"декабр€"
		};

		private static int GetMonth(string str)
		{
			int index = Array.IndexOf(months, str);
			if (index == -1)
			{
				throw new Exception($"Invalid month: {str}");
			}
			return index + 1;
		}

		private static TimeSpan GetTime(Match match)
		{
			string s1 = match.Groups[1].Value;
			int hour;

			try
			{
				hour = int.Parse(s1);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing hour '{s1}'.", ex);
			}

			string s2 = match.Groups[2].Value;

			int minutes;
			try
			{
				minutes = int.Parse(s2);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing minutes '{s2}'.", ex);
			}

			return new TimeSpan(
				hours: hour, 
				minutes: minutes, 
				seconds: 0);
		}


		private enum LinesContext
		{
			NeedDate,
			NeedTime,
			NeedText
		}
	}
}
