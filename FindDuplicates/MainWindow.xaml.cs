using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WPFFolderBrowser;

namespace FindDuplicates
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private List<FileInfo> GetFilesInDirectory(string directory)
        {
            var files = new List<FileInfo>();
            try
            {
                var directories = Directory.GetDirectories(directory);
                try
                {
                    var di = new DirectoryInfo(directory);
                    files.AddRange(di.GetFiles("*"));
                }
                catch
                {
                }
                foreach (var dir in directories)
                {
                    files.AddRange(GetFilesInDirectory(System.IO.Path.Combine(directory, dir)));
                }
            }
            catch
            {
            }

            return files;
        }

        private async Task<List<IGrouping<long, FileInfo>>> GetPossibleDuplicatesAsync(string selectedPath, bool takeSmallResultSet)
        {
            List<IGrouping<long, FileInfo>> files = null;
            await Task.Factory.StartNew(() =>
            {
                files = GetFilesInDirectory(selectedPath)
                               .OrderByDescending(f => f.Length)
                                 .GroupBy(f => f.Length)
                                 .Where(g => g.Count() > 1)
                                 .Take(takeSmallResultSet ? 100 : int.MaxValue)
                                 .ToList();
            });
            return files;
        }

        private async void StartClick(object sender, RoutedEventArgs e)
        {
            var takeSmallResultSet = ChkTakeSmallResultSet.IsChecked ?? false;
            var compareOnlyFirstPartOfFile = ChkCompareOnlyFirstPartOfFile.IsChecked ?? false;

            var fbd = new WPFFolderBrowserDialog();
            if (fbd.ShowDialog() != true)
                return;
            var sw = new Stopwatch();
            sw.Start();
            FilesList.ItemsSource = null;
            var selectedPath = fbd.FileName;

            var files = await GetPossibleDuplicatesAsync(selectedPath, takeSmallResultSet);
            var realDuplicates = await GetRealDuplicatesAsync(files, compareOnlyFirstPartOfFile);
            FilesList.ItemsSource = realDuplicates;
            sw.Stop();
            var allFiles = realDuplicates.SelectMany(f => f.Value).ToList();
            TotalFilesText.Text = $"{allFiles.Count} files found " +
                $"({allFiles.Sum(f => f.Length):N0} total duplicate bytes) {sw.ElapsedMilliseconds} ms";
        }

        private async Task<Dictionary<string, List<FileInfo>>> GetRealDuplicatesAsync(
            List<IGrouping<long, FileInfo>> files, bool compareOnlyFirstPartOfFile)
        {
            var dictFiles = new ConcurrentDictionary<string, ConcurrentBag<FileInfo>>();
            await Task.Factory.StartNew(() =>
            {
                Parallel.ForEach(files.SelectMany(g => g), file =>
                    {
                        var hash = GetMD5HashFromFile(file.FullName, compareOnlyFirstPartOfFile);
                        dictFiles.AddOrUpdate(hash, key => new ConcurrentBag<FileInfo>(new List<FileInfo> { file }),
                            (s, bag) =>
                            {
                                bag.Add(file);
                                return bag;
                            });
                    });
            });
            return dictFiles.Where(p => p.Value.Count > 1)
                .OrderByDescending(p => p.Value.First().Length)
                .ToDictionary(p => p.Key, p => p.Value.ToList());
        }

        public string GetMD5HashFromFile(string fileName, bool CompareOnlyFirstPartOfFile)
        {
            try
            {
                using (FileStream file = new FileStream(fileName, FileMode.Open))
                {
                    var md5 = new MD5CryptoServiceProvider();
                    byte[] retVal;
                    if (CompareOnlyFirstPartOfFile)
                    {
                        var bytes = new byte[10000];
                        var readBytes = file.Read(bytes, 0, 10000);
                        retVal = md5.ComputeHash(bytes, 0, readBytes);
                    }
                    else
                    {
                        retVal = md5.ComputeHash(file);
                    }

                    file.Close();
                    return BitConverter.ToString(retVal);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                return null;
            }
        }

        private void RemoveFile(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            if (button.DataContext is FileInfo file && (!(ChkCheckBeforeDelete.IsChecked ?? true) || MessageBox.Show($"Are you sure you want to delete {file.FullName}?", "SURE?", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes))
            {
                var compareOnlyFirstPartOfFile = ChkCompareOnlyFirstPartOfFile.IsChecked ?? false;
                var hash = GetMD5HashFromFile(file.FullName, compareOnlyFirstPartOfFile);
                file.Delete();
                var filesListItemsSource = (Dictionary<string, List<FileInfo>>)FilesList.ItemsSource;
                if (filesListItemsSource.TryGetValue(hash, out var item))
                {
                    FilesList.ItemsSource = null;
                    item.Remove(file);
                    if (item.Count == 1)
                    {
                        filesListItemsSource.Remove(hash);
                    }
                    FilesList.ItemsSource = filesListItemsSource;
                }
            }
        }
    }
}
