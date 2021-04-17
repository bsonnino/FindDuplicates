using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows;
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

        private async Task<List<IGrouping<long, FileInfo>>> GetPossibleDuplicatesAsync(string selectedPath)
        {
            List<IGrouping<long, FileInfo>> files = null;
            await Task.Factory.StartNew(() =>
            {
                files = GetFilesInDirectory(selectedPath)
                               .OrderByDescending(f => f.Length)
                                 .GroupBy(f => f.Length)
                                 .Where(g => g.Count() > 1)
                                 .Take(100)
                                 .ToList();
            });
            return files;
        }

        private async void StartClick(object sender, RoutedEventArgs e)
        {
            var fbd = new WPFFolderBrowserDialog();
            if (fbd.ShowDialog() != true)
                return;
            var sw = new Stopwatch();
            sw.Start();
            FilesList.ItemsSource = null;
            var selectedPath = fbd.FileName;

            var files = await GetPossibleDuplicatesAsync(selectedPath);
            var realDuplicates = await GetRealDuplicatesAsync(files);
            FilesList.ItemsSource = realDuplicates;
            sw.Stop();
            var allFiles = realDuplicates.SelectMany(f => f.Value).ToList();
            TotalFilesText.Text = $"{allFiles.Count} files found " +
                $"({allFiles.Sum(f => f.Length):N0} total duplicate bytes) {sw.ElapsedMilliseconds} ms";
        }

        private static async Task<Dictionary<string, List<FileInfo>>> GetRealDuplicatesAsync(
            List<IGrouping<long, FileInfo>> files)
        {
            var dictFiles = new ConcurrentDictionary<string, ConcurrentBag<FileInfo>>();
            await Task.Factory.StartNew(() =>
            {
                Parallel.ForEach(files.SelectMany(g => g), file =>
                    {
                        var hash = GetMD5HashFromFile(file.FullName);
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

        public static string GetMD5HashFromFile(string fileName)
        {
            try
            {
                using (FileStream file = new FileStream(fileName, FileMode.Open))
                {
                    var md5 = new MD5CryptoServiceProvider();
                    var retVal = md5.ComputeHash(file);
                    file.Close();
                    return BitConverter.ToString(retVal);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
                return null;
            }
        }
    }
}
