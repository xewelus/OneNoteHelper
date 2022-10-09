using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Animation;
using Common;
using Common.Classes.Diagnostic;
using CommonWpf;
using CommonWpf.Classes.UI;
using TestOneNote.Tests;

namespace OneNoteHelper
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			this.InitializeComponent();

			this.SaveResultButton.Visibility = Visibility.Hidden;

			this.ListView.Visibility = Visibility.Hidden;
			this.ListView.ItemsSource = null;
		}

		private static readonly Regex outerRegex = new Regex("([\\s\\S]*)(<html[\\s\\S]*<!--StartFragment-->)([\\s\\S]*)(<!--EndFragment-->[\\s\\S]*)");
		private static readonly string nbspString= "<p style='margin:0in'>&nbsp;</p>";
		private static readonly Regex nbspRegex = new Regex(nbspString);
		private static readonly Regex emptyRegex = new Regex("<p style='margin:0in;[\\s\\S-[\\r\\n]]*?>&nbsp;</p>");
		private static readonly Regex dateRegex = new Regex("<p style='[\\s\\S-[\\r\\n]]*?>(\\d+)\\s+(\\w+)\\s+(\\d+)\\s+г.</p>");
		private static readonly Regex timeRegex = new Regex("<p style='[\\s\\S-[\\r\\n]]*?>(\\d+):(\\d+)</p>");

		public List<Record> Records { get; set; }

		private bool isTest = false;
		private Match lastMatch;
		private void ProcessPages_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				string text;

				if (this.isTest)
				{
					text = TestResources.Test_html;
				}
				else
				{
					text = Clipboard.GetData("HTML Format") as string;

					if (string.IsNullOrEmpty(text))
					{
						UIHelper.ShowWarning("No OneNote data in clipboard.");
						return;
					}
				}

				this.ClipboardTextBox.Text = text;

				Match match = outerRegex.Match(text);
				if (!match.Success)
				{
					UIHelper.ShowWarning("Invalid OneNote data in clipboard.");
					Diag.SaveAndOpenLog(text);
					this.lastMatch = null;
					return;
				}

				this.lastMatch = match;

				string content = match.Groups[3].Value;
				content = content.Replace("\r\n", "\n");

				string[] parts = Regex.Split(content, "\n\n");

				List<Record> records = GetRecords(parts);

				records.Sort(CompareRecords);

				this.Records = records;
				this.ListView.ItemsSource = this.Records;
				this.ListView.Visibility = Visibility.Visible;

				this.SaveResultButton.Visibility = Visibility.Visible;
				this.SaveResultButton.IsEnabled = records.Any(r => !r.HasError);
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}

		private static List<Record> GetRecords(string[] parts)
		{
			List<PartInfo> partInfoList = GetPartsInfoList(parts);
			List<PartInfo> partInfoList2 = PreCleanPartsInfoList(partInfoList);
			List<PartInfo> partInfoList3 = FindRecords(partInfoList2);
			List<PartInfo> partInfoList4 = FindRecordsAfterDoubleNbsp(partInfoList3);
			List<PartInfo> partInfoList5 = FindRecords(partInfoList4);
			List<PartInfo> partInfoList6 = FindNbspDateNbspContent(partInfoList5);
			List<PartInfo> partInfoList7 = FindRecords(partInfoList6);

			List<Record> records = new List<Record>();

			foreach (PartInfo partInfo in partInfoList7)
			{
				try
				{
					if (partInfo.PartType.In(PartType.Nbsp, PartType.DoubleNbsp)) continue;

					Record record = new Record();
					record.Date = partInfo.Date ?? DateTime.MinValue;
					record.DateString = partInfo.DateString ?? "No date";
					record.Block = partInfo.Text?.Replace("\n", "\r\n");
					record.Text = record.Block;
					records.Add(record);

					record.Index = records.Count;

					if (partInfo.PartType == PartType.Record)
					{
						continue;
					}

					if (partInfo.PartType == PartType.Content)
					{
						record.Error = new Exception("No date for record.");
						continue;
					}

					throw new NotSupportedException($"Part type '{partInfo.PartType}' is not supported.");
				}
				catch (Exception ex)
				{
					string lastLinesStr = GetLastPartsContent(partInfoList);
					throw new Exception($"Error processing part '{partInfo}' after lines:\r\n{lastLinesStr}", ex);
				}
			}

			return records;
		}

		private static List<PartInfo> GetPartsInfoList(string[] parts)
		{
			PartInfo partInfo = null;
			List<PartInfo> partInfoList = new List<PartInfo>();

			List<string> lastLines = new List<string>();

			foreach (string part in parts)
			{
				try
				{
					string[] lines = part.Split('\n');

					for (int lineNum = 0; lineNum < lines.Length; lineNum++)
					{
						string line = lines[lineNum];
						try
						{
							lastLines.Add(line);
							while (lastLines.Count > 20)
							{
								lastLines.RemoveAt(0);
							}

							if (line.Length == 0)
							{
								partInfo = new PartInfo(partInfoList.Count, PartType.EmptyLine, line);
								partInfoList.Add(partInfo);
								partInfo = null;

								continue;
							}

							if (nbspRegex.IsMatch(line))
							{
								partInfo = new PartInfo(partInfoList.Count, PartType.Nbsp, line);
								partInfoList.Add(partInfo);
								partInfo = null;

								if (lines.Length > 1)
								{
									throw new Exception($"Invalid 'nsbp' with many lines:\r\n{part}");
								}

								continue;
							}

							if (line.StartsWith("<p "))
							{
								// append all lines until empty because of line wrapping
								while (lineNum < lines.Length - 1)
								{
									string nextLine = lines[lineNum + 1];
									if (nextLine.Length == 0) break;

									line = $"{line} {nextLine}";
									lineNum++;
								}
							}

							Match match = dateRegex.Match(line);
							if (match.Success)
							{
								partInfo = new PartInfo(partInfoList.Count, PartType.Date, line);
								partInfoList.Add(partInfo);

								(partInfo.Date, partInfo.DateString) = GetDay(match);
								partInfo = null;

								continue;
							}

							Match timeMatch = timeRegex.Match(line);
							if (timeMatch.Success)
							{
								partInfo = new PartInfo(partInfoList.Count, PartType.Time, line);
								partInfoList.Add(partInfo);

								partInfo.Time = GetTime(timeMatch);
								partInfo = null;

								continue;
							}

							if (emptyRegex.IsMatch(line))
							{
								partInfo = new PartInfo(partInfoList.Count, PartType.EmptyContentLine, line);
								partInfoList.Add(partInfo);
								partInfo = null;

								continue;
							}

							if (partInfo == null)
							{
								partInfo = new PartInfo(partInfoList.Count, PartType.Content);
								partInfoList.Add(partInfo);
							}

							partInfo.Sb.AppendLine(line);
						}
						catch (Exception ex)
						{
							throw new Exception($"Error while processing line #{lineNum}: {line}", ex);
						}
					}
				}
				catch (Exception ex)
				{
					string lastLinesStr = GetLastPartsContent(partInfoList);
					throw new Exception($"Error while process part '{part}' after lines:\r\n{lastLinesStr}", ex);
				}
			}

			return partInfoList;
		}

		private static List<PartInfo> PreCleanPartsInfoList(List<PartInfo> partInfoList)
		{
			List<PartInfo> result = new List<PartInfo>(partInfoList);

			while (result.Count > 0 && result[0].PartType == PartType.EmptyLine)
			{
				result.RemoveAt(0);
			}

			while (result.Count > 0 && result.Last().PartType.In(PartType.EmptyLine, PartType.EmptyContentLine))
			{
				result.RemoveAt(result.Count - 1);
			}

			for (int i = 0; i < result.Count; i++)
			{
				PartInfo partInfo = result[i];

				try
				{
					if (partInfo.PartType == PartType.Date)
					{
						if (i == 0)
						{
							throw new Exception("Date on first line.");
						}

						PartInfo prevInfo = result[i - 1];

						if (i >= result.Count - 1)
						{
							throw new Exception("Date on last line.");
						}

						PartInfo nextInfo = result[i + 1];
						if (nextInfo.PartType != PartType.Time)
						{
							throw new Exception($"Not found time part after date. Next line: {nextInfo}");
						}

						PartInfo newInfo;
						if (prevInfo.PartType == PartType.EmptyContentLine)
						{
							result.RemoveRange(i - 1, 3);
							newInfo = new PartInfo(
								index: prevInfo.Index,
								partType: PartType.DateWithTime,
								line: $"{prevInfo.Text}\n{partInfo.Text}\n{nextInfo.Text}");
							result.Insert(i - 1, newInfo);
						}
						else
						{
							// if not empty line - date block placed after content
							result.RemoveRange(i, 2);
							newInfo = new PartInfo(
								index: partInfo.Index,
								partType: PartType.DateWithTimeWithoutLine,
								line: $"{partInfo.Text}\n{nextInfo.Text}");
							result.Insert(i, newInfo);
						}

						if (partInfo.Date == null) throw new NullReferenceException(nameof(partInfo.Date));
						if (nextInfo.Time == null) throw new NullReferenceException(nameof(nextInfo.Time));
						newInfo.Date = partInfo.Date.Value.Add(nextInfo.Time.Value);
						newInfo.DateString = partInfo.DateString;

						i--;

						continue;
					}

					if (partInfo.PartType == PartType.Nbsp)
					{
						if (i == 0) continue;

						PartInfo prevInfo = result[i - 1];
						if (prevInfo.PartType == PartType.EmptyContentLine)
						{
							result.RemoveAt(i - 1);
							i--;
							continue;
						}

						if (prevInfo.PartType == PartType.Nbsp)
						{
							result.RemoveRange(i - 1, 2);

							PartInfo newInfo = new PartInfo(prevInfo.Index, PartType.DoubleNbsp);
							result.Insert(i - 1, newInfo);
						}

						//if (prevInfo.PartType == PartType.DateWithTime)
						//{
						//	result.RemoveAt(i);
						//}
					}
				}
				catch (Exception ex)
				{
					string lastLinesStr = GetLastPartsContent(partInfoList);
					throw new Exception($"Error processing part '{partInfo}' after lines:\r\n{lastLinesStr}", ex);
				}
			}
			return result;
		}

		private static List<PartInfo> FindRecords(List<PartInfo> partInfoList)
		{
			List<PartInfo> result = new List<PartInfo>();

			PartInfo recordInfo = null;

			foreach (PartInfo partInfo in partInfoList)
			{
				try
				{
					if (partInfo.PartType.In(PartType.Nbsp, PartType.DoubleNbsp))
					{
						AddRecordInfo();

						result.Add(partInfo);
						continue;
					}

					if (partInfo.PartType == PartType.Record)
					{
						result.Add(partInfo);
						continue;
					}

					if (recordInfo == null)
					{
						recordInfo = new PartInfo(partInfo.Index, PartType.Record);
						recordInfo.RecordSubParts = new List<PartInfo>();
					}

					if (partInfo.PartType.In(PartType.DateWithTime, PartType.DateWithTimeWithoutLine))
					{
						if (recordInfo.RecordDateInfo != null)
						{
							throw new Exception("Duplicate date part.");
						}
						recordInfo.RecordDateInfo = partInfo;
						recordInfo.Date = partInfo.Date;
						recordInfo.DateString = partInfo.DateString;
						continue;
					}

					if (partInfo.PartType.In(PartType.Content, PartType.EmptyContentLine))
					{
						recordInfo.RecordSubParts.Add(partInfo);
						continue;
					}

					if (partInfo.PartType == PartType.EmptyLine)
					{
						continue;
					}

					throw new NotSupportedException($"Unsupported part type '{partInfo.PartType}'.");
				}
				catch (Exception ex)
				{
					string lastLinesStr = GetLastPartsContent(partInfoList);
					throw new Exception($"Error processing part '{partInfo}' after lines:\r\n{lastLinesStr}", ex);
				}
			}

			AddRecordInfo();

			void AddRecordInfo()
			{
				if (recordInfo != null)
				{
					List<PartInfo> subParts = recordInfo.RecordSubParts;

					while (subParts.Count > 0 && subParts[0].PartType == PartType.EmptyContentLine)
					{
						subParts.RemoveAt(0);
					}

					while (subParts.Count > 0 && subParts.Last().PartType == PartType.EmptyContentLine)
					{
						subParts.RemoveLast();
					}

					if (recordInfo.RecordDateInfo == null)
					{
						result.AddRange(subParts);
					}
					else if (subParts.Count == 0)
					{
						result.Add(recordInfo.RecordDateInfo);
					}
					else
					{
						recordInfo.Sb.AppendLine(string.Join("\n\n", subParts.Select(p => p.Text)));
						result.Add(recordInfo);
					}
					recordInfo = null;
				}
			}

			return result;
		}

		private static List<PartInfo> FindRecordsAfterDoubleNbsp(List<PartInfo> partInfoList)
		{
			List<PartInfo> result = new List<PartInfo>(partInfoList);

			for (int i = 0; i < partInfoList.Count - 3; i++)
			{
				PartInfo partInfo = partInfoList[i];

				try
				{
					PartInfo partInfo2 = partInfoList[i + 1];
					PartInfo partInfo3 = partInfoList[i + 2];
					PartInfo partInfo4 = partInfoList[i + 3];

					if (partInfo.PartType == PartType.DoubleNbsp
					    && partInfo2.PartType == PartType.DateWithTime
					    && partInfo3.PartType == PartType.Nbsp
					    && partInfo4.PartType == PartType.Content)
					{
						result.Remove(partInfo3);
					}
				}
				catch (Exception ex)
				{
					string lastLinesStr = GetLastPartsContent(partInfoList);
					throw new Exception($"Error processing part '{partInfo}' after lines:\r\n{lastLinesStr}", ex);
				}
			}

			return result;
		}

		private static List<PartInfo> FindNbspDateNbspContent(List<PartInfo> partInfoList)
		{
			List<PartInfo> result = new List<PartInfo>(partInfoList);

			for (int i = 0; i < partInfoList.Count - 3; i++)
			{
				PartInfo partInfo = partInfoList[i];

				try
				{
					PartInfo partInfo2 = partInfoList[i + 1];
					PartInfo partInfo3 = partInfoList[i + 2];
					PartInfo partInfo4 = partInfoList[i + 3];

					if (partInfo.PartType == PartType.Nbsp
						&& partInfo2.PartType == PartType.DateWithTime
					    && partInfo3.PartType == PartType.Nbsp
					    && partInfo4.PartType == PartType.Content)
					{
						result.Remove(partInfo3);
					}
				}
				catch (Exception ex)
				{
					string lastLinesStr = GetLastPartsContent(partInfoList);
					throw new Exception($"Error processing part '{partInfo}' after lines:\r\n{lastLinesStr}", ex);
				}
			}

			return result;
		}

		private static (DateTime Date, string DateString) GetDay(Match match)
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

			return (new DateTime(year, month, day), $"{s1} {s2} {s3} г.");
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

		private static string GetLastPartsContent(List<PartInfo> partInfoList)
		{
			StringBuilder sb = new StringBuilder();
			int maxCount = 10;
			int count = 0;
			int from = Math.Max(0, partInfoList.Count - maxCount);
			for (int i = partInfoList.Count - 1; i >= from; i--)
			{
				PartInfo info = partInfoList[i];
				if (info.PartType == PartType.Record)
				{
					for (int j = info.RecordSubParts.Count - 1; j >= 0; j--)
					{
						sb.AppendLine(info.RecordSubParts[j].Text).AppendLine();
						if (count++ >= maxCount) break;
					}

					if (count >= maxCount) break;
				}
				else
				{
					sb.AppendLine(info.Text).AppendLine();
					if (count++ >= maxCount) break;
				}
			}
			return sb.ToString();
		}

		private static int CompareRecords(Record r1, Record r2)
		{
			int res = Comparer<bool>.Default.Compare(r1.Error == null, r2.Error == null);
			if (res != 0) return res;

			if (r1.Error == null)
			{
				res = Comparer<DateTime>.Default.Compare(r1.Date, r2.Date);
				if (res != 0) return res;
			}

			return Comparer<int>.Default.Compare(r1.Index, r2.Index);
		}

		private void SaveResultButton_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				int errorsCount = this.Records.Count(r => r.HasError);
				if (errorsCount > 0)
				{
					if (!UIHelper.AskYesNo($"Results contains some errors ({errorsCount}). Continue?"))
					{
						return;
					}
				}

				StringBuilder sb = new StringBuilder();

				// beginning part
				sb.AppendLine(this.lastMatch.Groups[2].Value);

				sb.AppendLine("<table border=1 cellpadding=0 cellspacing=0 valign=top>");
				foreach (Record record in this.Records)
				{
					sb.AppendLine("<tr>");
					sb.AppendLine("<td>");
					sb.AppendLine($"<p style='margin:0in;font-family:Consolas;font-size:10.0pt'>{record.DateString}</p>");
					sb.AppendLine($"<p style='margin:0in;font-family:Consolas;font-size:10.0pt'>{record.Date:HH:mm}</p>");

					const string newLine = "<p style='margin:0in;font-family:Consolas;font-size:10.0pt' lang=x-none>&nbsp;</p>";
					sb.AppendLine(newLine);

					sb.AppendLine(record.Text);

					if (record.Error != null)
					{
						sb.AppendLine(newLine);
						sb.AppendLine(record.Error.ToString());
					}

					sb.AppendLine("</td>");
					sb.AppendLine("<td>");
					sb.AppendLine("</td>");
					sb.AppendLine("</tr>");
				}
				sb.AppendLine("</table>");

				// ending part
				sb.AppendLine(this.lastMatch.Groups[4].Value);

				Diag.SaveAndOpenLog(sb.ToString(), "result_", ".html");
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}

		private enum LinesContext
		{
			Default,
			NeedTime,
			NeedText
		}

		private class PartInfo
		{
			public readonly int Index;
			public readonly PartType PartType;
			public DateTime? Date;
			public string DateString;
			public TimeSpan? Time;
			public readonly StringBuilder Sb = new StringBuilder();

			public List<PartInfo> RecordSubParts;
			public PartInfo RecordDateInfo;

			public string Text
			{
				get
				{
					return this.Sb.ToString();
				}
			}

			public PartInfo(int index, PartType partType, string line = null)
			{
				this.Index = index;
				this.PartType = partType;

				if (line != null)
				{
					this.Sb.Append(line);
				}
			}

			public override string ToString()
			{
				string result = $"#{this.Index} {this.PartType}";
				if (this.PartType == PartType.Date)
				{
					result = $"{result} {this.Date?.ToString("dd.MM.yyyy")}";
				}
				else if (this.PartType == PartType.Time)
				{
					result = $"{result} {this.Time?.Hours}:{this.Time?.Minutes}";
				}
				else if (this.PartType.In(PartType.DateWithTime, PartType.DateWithTimeWithoutLine))
				{
					result = $"{result} {this.Date?.ToString("dd.MM.yyyy HH:mm")}";
				}
				else if (this.PartType == PartType.Content)
				{
					result = $"{result} {this.Sb}";
				}
				else if (this.PartType == PartType.Record)
				{
					result = $"{result} {this.Date?.ToString("dd.MM.yyyy HH:mm")} {this.Sb}";
				}
				return result;
			}
		}

		private enum PartType
		{
			DoubleNbsp,
			Nbsp,
			Date,
			Time,
			DateWithTime,
			DateWithTimeWithoutLine,
			Content,
			EmptyLine,
			EmptyContentLine,
			Record
		}

	}
}
