﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.LibraryManager.Contracts;

namespace Microsoft.Web.LibraryManager.Vsix.UI.Controls
{
    public partial class Library : INotifyPropertyChanged
    {
        public static readonly DependencyProperty CaretIndexProperty = DependencyProperty.Register(
            nameof(CaretIndex), typeof(int), typeof(Library), new PropertyMetadata(default(int), SearchCriteriaChanged));

        public static readonly DependencyProperty SearchServiceProperty = DependencyProperty.Register(
            nameof(SearchService), typeof(Func<string, int, Task<CompletionSet>>), typeof(Library), new PropertyMetadata(default(Func<string, int, Task<CompletionSet>>), SearchCriteriaChanged));

        public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
            nameof(SelectedItem), typeof(Completion), typeof(Library), new PropertyMetadata(default(Completion)));

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(Library), new PropertyMetadata(default(string), SearchCriteriaChanged));

        private int _version;

        public Library()
        {
            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public int CaretIndex
        {
            get { return (int)GetValue(CaretIndexProperty); }
            set { SetValue(CaretIndexProperty, value); }
        }

        public bool IsMouseOverFlyout => Options.IsMouseOver;

        public bool IsTextEntryEmpty => string.IsNullOrEmpty(Text);

        public bool HasItems => Items.Count > 0;

        public ObservableCollection<Completion> Items { get; } = new ObservableCollection<Completion>();

        public Func<string, int, Task<CompletionSet>> SearchService
        {
            get { return (Func<string, int, Task<CompletionSet>>)GetValue(SearchServiceProperty); }
            set { SetValue(SearchServiceProperty, value); }
        }

        public Completion SelectedItem
        {
            get { return (Completion)GetValue(SelectedItemProperty); }
            set { SetValue(SelectedItemProperty, value); }
        }

        public string Text
        {
            get { return (string)GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static void SearchCriteriaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            Library search = d as Library;
            search?.RefreshSearch();
            search?.PropertyChanged?.Invoke(search, new PropertyChangedEventArgs(nameof(IsTextEntryEmpty)));
        }

        private void Commit(Completion completion)
        {
            if (completion == null)
            {
                return;
            }

            Text = completion.CompletionItem.InsertionText;
            LibrarySearchBox.CaretIndex = Text.IndexOf(completion.CompletionItem.DisplayText, StringComparison.OrdinalIgnoreCase) + completion.CompletionItem.DisplayText.Length;
        }

        private void HandleKeyPress(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Tab:
                case Key.Enter:
                    Commit(SelectedItem);
                    break;
                case Key.Escape:
                    e.Handled = true;
                    LibrarySearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    break;
                case Key.Down:
                    if (Options.Items.Count > 0)
                    {
                        Options.ScrollIntoView(Options.Items[0]);
                        FrameworkElement fe = (FrameworkElement)Options.ItemContainerGenerator.ContainerFromIndex(0);
                        fe?.Focus();
                        Options.SelectedIndex = 0;
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void HandleKeyUp(object sender, KeyEventArgs e)
        {
            CaretIndex = LibrarySearchBox.CaretIndex;
            RefreshSearch();
        }

        private void HandleListBoxKeyPress(object sender, KeyEventArgs e)
        {
            int index = LibrarySearchBox.CaretIndex;

            switch (e.Key)
            {
                case Key.Tab:
                case Key.Enter:
                    Commit(SelectedItem);
                    e.Handled = true;
                    LibrarySearchBox.Focus();
                    break;
                case Key.Up:
                    if (Options.SelectedIndex == 0)
                    {
                        SelectedItem = Items[0];
                        LostFocus -= OnLostFocus;
                        LibrarySearchBox.Focus();
                        LibrarySearchBox.CaretIndex = index;
                        LostFocus += OnLostFocus;
                    }
                    break;
                case Key.Escape:
                    e.Handled = true;
                    LibrarySearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    break;
                case Key.Down:
                case Key.PageDown:
                case Key.PageUp:
                case Key.Home:
                case Key.End:
                    break;
                default:
                    LostFocus -= OnLostFocus;
                    LibrarySearchBox.Focus();
                    LibrarySearchBox.CaretIndex = index;
                    LostFocus += OnLostFocus;
                    break;
            }
        }

        private void OnItemCommitGesture(object sender, MouseButtonEventArgs e)
        {
            Commit(SelectedItem);
            e.Handled = true;
            LibrarySearchBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (SelectedItem != null && !Options.IsKeyboardFocusWithin)
            {
                Commit(SelectedItem);
            }
        }

        private void OnMousePositionCaret(object sender, MouseButtonEventArgs e)
        {
            if (CaretIndex != LibrarySearchBox.CaretIndex)
            {
                CaretIndex = LibrarySearchBox.CaretIndex;
                RefreshSearch();
            }
        }

        private void PositionCompletions(int index)
        {
            Rect r = LibrarySearchBox.GetRectFromCharacterIndex(index);
            Flyout.HorizontalOffset = r.Left - 7;
            Options.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Flyout.Width = Options.DesiredSize.Width;
        }

        internal void RefreshSearch()
        {
            if (Text == null)
            {
                return;
            }

            string lastSelected = SelectedItem?.CompletionItem.InsertionText;
            int expect = Interlocked.Increment(ref _version);

            string text = Text;
            int caretIndex = text.Length;
            int atIndex = text.IndexOf('@');
            Func<string, int, Task<CompletionSet>> searchService = SearchService;
            Task.Delay(250).ContinueWith(d => 
            {
                if (Volatile.Read(ref _version) != expect)
                {
                    return;
                }

                Dispatcher.BeginInvoke((Action)(() =>
                {
                    searchService?.Invoke(text, caretIndex).ContinueWith(t =>
                    {
                        if (t.IsCanceled || t.IsFaulted)
                        {
                            return;
                        }

                        CompletionSet span = t.Result;

                        Dispatcher.BeginInvoke((Action)(() =>
                        {
                            if (Volatile.Read(ref _version) != expect || span.Completions == null)
                            {
                                return;
                            }

                            Items.Clear();

                            if (atIndex >= 0)
                            {
                                span.Completions = FilterOutUnmatchedItems(span.Completions, text.Substring(atIndex + 1));
                            }

                            foreach (CompletionItem entry in span.Completions)
                            {
                                Items.Add(new Completion(entry, span.Start, span.Length));
                            }

                            PositionCompletions(span.Length);
                            OnPropertyChanged(nameof(HasItems));

                            if (Items != null && Items.Count > 0 && Options.SelectedIndex == -1)
                            {
                                if (atIndex >= 0)
                                {
                                    SelectedItem = Items.FirstOrDefault(x => x.CompletionItem.DisplayText.StartsWith(text.Substring(atIndex + 1))) ?? Items[0];
                                }
                                else
                                {
                                    SelectedItem = Items.FirstOrDefault(x => x.CompletionItem.InsertionText == lastSelected) ?? Items[0];
                                }

                                Options.ScrollIntoView(SelectedItem);
                            }
                        }));
                    });
                }));
            });
        }

        private void ThisControl_GotFocus(object sender, RoutedEventArgs e)
        {
            if (!Options.IsKeyboardFocusWithin && !LibrarySearchBox.IsKeyboardFocusWithin && !Flyout.IsKeyboardFocusWithin)
            {
                LibrarySearchBox.Focus();
            }
        }

        private IEnumerable<CompletionItem> FilterOutUnmatchedItems(IEnumerable<CompletionItem>items, string versionSuffix)
        {
            return items.Where(x => x.DisplayText.Contains(versionSuffix));
        }
    }
}
