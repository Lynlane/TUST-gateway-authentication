using System;
using System.IO;
using LibGit2Sharp;
using System.Diagnostics;

namespace GitRepoDownloader
{
    public class DataUpdater
    {
        private const string GIT_REPO_URL = "https://gitee.com/Lynlane/TUST-gateway-authentication.git";
        private const string CACHE_DIR = "cache";
        private const string DATA_DIR = "res\\data";
        private const string VERSION_FILE = "version.txt";
        private const string BRANCH = "hotfix";

        // 删除目录
        private void DeleteDirectoryWithReadOnlyFiles(string path)
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in Directory.GetFiles(path, "*.*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(path, true);
        }

        /// <summary>
        /// 执行 Git 命令（并暂时调整git全局代理）
        /// </summary>
        private void ExecuteGitCommand(string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process.Start();
                process.WaitForExit();
            }
        }

        /// <summary>
        /// 更新检查结果类
        /// </summary>
        public class UpdateCheckResult
        {
            public bool HasAppUpdate { get; set; }
            public bool HasDataUpdate { get; set; }
            public string RemoteAppVersion { get; set; }
            public string LocalAppVersion { get; set; }
            public ulong RemoteDataVersion { get; set; }
            public ulong LocalDataVersion { get; set; }
        }

        /// <summary>
        /// 检查更新（异步）
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            return await Task.Run(() =>
            {
                var result = new UpdateCheckResult();

                try
                {
                    DeleteDirectoryWithReadOnlyFiles(CACHE_DIR);
                    Directory.CreateDirectory(CACHE_DIR);

                    string appDir = GetApplicationRootDirectory();
                    string localVersionPath = Path.Combine(appDir, DATA_DIR, VERSION_FILE);

                    Console.WriteLine($"检查版本文件: 应用目录={appDir}");
                    Console.WriteLine($"版本文件绝对路径={localVersionPath}, 存在={File.Exists(localVersionPath)}");

                    // 读取本地版本信息
                    VersionInfo localVersionInfo = new VersionInfo();
                    if (File.Exists(localVersionPath))
                    {
                        string localVersionContent = File.ReadAllText(localVersionPath).Trim();
                        localVersionInfo = ParseVersionFile(localVersionContent);
                        Console.WriteLine($"本地版本文件存在，APP版本={localVersionInfo.AppVersion}, DATA版本={localVersionInfo.DataVersion}");
                    }
                    else
                    {
                        Console.WriteLine("本地版本文件不存在，直接认为有更新");
                    }

                    // 获取远程版本信息
                    VersionInfo remoteVersionInfo = GetRemoteVersionFile();

                    if (remoteVersionInfo != null)
                    {
                        // 比较APP版本
                        result.HasAppUpdate = !string.Equals(localVersionInfo.AppVersion, remoteVersionInfo.AppVersion, StringComparison.OrdinalIgnoreCase);
                        // 比较DATA版本
                        result.HasDataUpdate = localVersionInfo.DataVersion < remoteVersionInfo.DataVersion;

                        // 设置版本信息
                        result.RemoteAppVersion = remoteVersionInfo.AppVersion;
                        result.LocalAppVersion = localVersionInfo.AppVersion;
                        result.RemoteDataVersion = remoteVersionInfo.DataVersion;
                        result.LocalDataVersion = localVersionInfo.DataVersion;

                        Console.WriteLine($"版本比较结果: HasAppUpdate={result.HasAppUpdate} (远程APP版本{remoteVersionInfo.AppVersion} != 本地APP版本{localVersionInfo.AppVersion})");
                        Console.WriteLine($"版本比较结果: HasDataUpdate={result.HasDataUpdate} (远程DATA版本{remoteVersionInfo.DataVersion} > 本地DATA版本{localVersionInfo.DataVersion})");
                    }
                    else
                    {
                        // 无法获取远程版本，认为有更新
                        result.HasAppUpdate = true;
                        result.HasDataUpdate = true;
                        Console.WriteLine("无法获取远程版本信息，默认认为有更新");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"检查更新时出错: {ex.Message}");
                    // 出错时默认认为有更新
                    result.HasAppUpdate = true;
                    result.HasDataUpdate = true;
                }
                finally
                {
                    DeleteDirectoryWithReadOnlyFiles(CACHE_DIR);
                }

                return result;
            });
        }

        /// <summary>
        /// 检查更新（同步，兼容旧代码）
        /// </summary>
        public UpdateCheckResult CheckForUpdates()
        {
            return CheckForUpdatesAsync().Result;
        }


        /// <summary>
        /// 版本信息类
        /// </summary>
        public class VersionInfo
        {
            public string AppVersion { get; set; }
            public ulong DataVersion { get; set; }
        }

        /// <summary>
        /// 解析版本文件内容
        /// </summary>
        private VersionInfo ParseVersionFile(string versionContent)
        {
            var lines = versionContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var versionInfo = new VersionInfo();

            foreach (var line in lines)
            {
                if (line.StartsWith("APP_VERSION="))
                {
                    versionInfo.AppVersion = line.Substring("APP_VERSION=".Length).Trim();
                }
                else if (line.StartsWith("DATA_VERSION="))
                {
                    if (ulong.TryParse(line.Substring("DATA_VERSION=".Length).Trim(), out ulong dataVersion))
                    {
                        versionInfo.DataVersion = dataVersion;
                    }
                }
            }

            return versionInfo;
        }

        /// <summary>
        /// 获取远程版本文件
        /// </summary>
        private VersionInfo GetRemoteVersionFile()
        {
            try
            {
                // 临时取消 Git 全局代理（克隆前）
                string oldHttpProxy = ExecuteGitCommandAndGetOutput("config --global --get http.proxy");
                string oldHttpsProxy = ExecuteGitCommandAndGetOutput("config --global --get https.proxy");
                ExecuteGitCommand("config --global --unset http.proxy");
                ExecuteGitCommand("config --global --unset https.proxy");

                // 稀疏检出拉取 version.txt
                var cloneOptions = new CloneOptions
                {
                    Checkout = false,
                    BranchName = BRANCH
                };

                // 克隆仓库
                Repository.Clone(GIT_REPO_URL, CACHE_DIR, cloneOptions);

                using (var repo = new Repository(CACHE_DIR))
                {
                    // 启用稀疏检出
                    repo.Config.Set("core.sparseCheckout", true);

                    // 写入稀疏检出规则（仅拉取 version.txt）
                    string sparseCheckoutPath = Path.Combine(repo.Info.Path, "info", "sparse-checkout");
                    File.WriteAllText(sparseCheckoutPath, VERSION_FILE);

                    // 检出分支并应用稀疏检出
                    Commands.Checkout(repo, repo.Branches[$"origin/{BRANCH}"], new CheckoutOptions());
                }

                // 恢复 Git 全局代理（克隆后）
                if (!string.IsNullOrEmpty(oldHttpProxy))
                    ExecuteGitCommand($"config --global http.proxy {oldHttpProxy}");
                if (!string.IsNullOrEmpty(oldHttpsProxy))
                    ExecuteGitCommand($"config --global https.proxy {oldHttpsProxy}");

                // 读取 version.txt
                string versionFile = Path.Combine(CACHE_DIR, VERSION_FILE);
                if (File.Exists(versionFile))
                {
                    string versionContent = File.ReadAllText(versionFile).Trim();
                    return ParseVersionFile(versionContent);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取远程版本文件失败: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 执行更新（异步）
        /// </summary>
        public async Task UpdateAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    DeleteDirectoryWithReadOnlyFiles(CACHE_DIR);

                    // 临时取消 Git 全局代理（克隆前）
                    string oldHttpProxy = ExecuteGitCommandAndGetOutput("config --global --get http.proxy");
                    string oldHttpsProxy = ExecuteGitCommandAndGetOutput("config --global --get https.proxy");
                    ExecuteGitCommand("config --global --unset http.proxy");
                    ExecuteGitCommand("config --global --unset https.proxy");

                    var cloneOptions = new CloneOptions
                    {
                        BranchName = BRANCH,
                        Checkout = true
                    };

                    // 克隆仓库
                    Repository.Clone(GIT_REPO_URL, CACHE_DIR, cloneOptions);

                    // 恢复 Git 全局代理（克隆后）
                    if (!string.IsNullOrEmpty(oldHttpProxy))
                        ExecuteGitCommand($"config --global http.proxy {oldHttpProxy}");
                    if (!string.IsNullOrEmpty(oldHttpsProxy))
                        ExecuteGitCommand($"config --global https.proxy {oldHttpsProxy}");

                    string currentDir = Directory.GetCurrentDirectory();
                    string targetDir = Path.Combine(currentDir, DATA_DIR);

                    if (!Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    foreach (string file in Directory.GetFiles(CACHE_DIR, "*.*", SearchOption.AllDirectories))
                    {
                        if (file.Contains(".git")) continue;

                        string relativePath = file.Substring(CACHE_DIR.Length + 1);
                        string destPath = Path.Combine(targetDir, Path.GetFileName(relativePath));

                        if (Path.GetFileName(relativePath).Equals(VERSION_FILE, StringComparison.OrdinalIgnoreCase))
                        {
                            // 对于version.txt文件，只更新DATA_VERSION的值
                            if (File.Exists(destPath))
                            {
                                // 读取本地version.txt的内容
                                string localVersionContent = File.ReadAllText(destPath);
                                VersionInfo localVersionInfo = ParseVersionFile(localVersionContent);
                                
                                // 读取远程version.txt的内容
                                string remoteVersionContent = File.ReadAllText(file);
                                VersionInfo remoteVersionInfo = ParseVersionFile(remoteVersionContent);
                                
                                // 创建新的version.txt内容，保留本地APP_VERSION，更新DATA_VERSION
                                string newVersionContent = $"APP_VERSION={localVersionInfo.AppVersion}\r\nDATA_VERSION={remoteVersionInfo.DataVersion}";
                                
                                // 写入新的version.txt内容
                                File.WriteAllText(destPath, newVersionContent);
                            }
                            else
                            {
                                // 如果本地没有version.txt文件，创建新文件，只包含DATA_VERSION，APP_VERSION留空
                                string remoteVersionContent = File.ReadAllText(file);
                                VersionInfo remoteVersionInfo = ParseVersionFile(remoteVersionContent);
                                
                                // 创建新的version.txt内容，APP_VERSION留空，只写入DATA_VERSION
                                string newVersionContent = $"APP_VERSION=\r\nDATA_VERSION={remoteVersionInfo.DataVersion}";
                                
                                // 写入新的version.txt内容
                                File.WriteAllText(destPath, newVersionContent);
                            }
                        }
                        else
                        {
                            // 对于其他文件，直接复制
                            if (File.Exists(destPath))
                            {
                                File.Delete(destPath);
                            }
                            File.Copy(file, destPath, true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"更新过程中出错: {ex.Message}");
                    throw;
                }
                finally
                {
                    DeleteDirectoryWithReadOnlyFiles(CACHE_DIR);
                }
            });
        }

        /// <summary>
        /// 执行更新（同步，兼容旧代码）
        /// </summary>
        public void Update()
        {
            UpdateAsync().Wait();
        }

        /// <summary>
        /// 执行 Git 命令并获取输出
        /// </summary>
        private string ExecuteGitCommandAndGetOutput(string arguments)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                process.Start();
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return output;
            }
        }

        /// <summary>
        /// 获取应用程序根目录
        /// </summary>
        private string GetApplicationRootDirectory()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            while (appDir.EndsWith("\\") || appDir.EndsWith("\\bin\\Debug\\net9.0\\"))
            {
                appDir = Directory.GetParent(appDir)?.FullName ?? appDir;
            }
            return appDir;
        }
    }
}