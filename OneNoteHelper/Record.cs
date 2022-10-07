using System;
using System.Windows;

namespace OneNoteHelper;

public class Record : DependencyObject
{
	public DateTime Date { get; set; }
	public string Text { get; set; }
}