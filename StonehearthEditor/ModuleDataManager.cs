﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace StonehearthEditor
{
    public class ModuleDataManager : IDisposable
    {
        private static ModuleDataManager sInstance = null;

        public static ModuleDataManager GetInstance()
        {
            return sInstance;
        }

        private string mModsDirectoryPath;
        private Dictionary<string, Module> mModules = new Dictionary<string, Module>();

        private HashSet<FileData> mFilesWithErrors = new HashSet<FileData>();
        private Dictionary<string, int> mAverageMaterialCost = new Dictionary<string, int>();

        public ModuleDataManager(string modsDirectoryPath)
        {
            if (sInstance != null)
            {
                sInstance.Dispose();
                sInstance = null;
            }

            mModsDirectoryPath = JsonHelper.NormalizeSystemPath(modsDirectoryPath);
            sInstance = this;
        }

        public string ModsDirectoryPath
        {
            get { return mModsDirectoryPath; }
        }

        public bool HasErrors
        {
            get
            {
                return mFilesWithErrors.Count > 0;
            }
        }

        public void AddErrorFile(FileData fileWithError)
        {
            mFilesWithErrors.Add(fileWithError);
        }

        public HashSet<FileData> GetErrorFiles()
        {
            return mFilesWithErrors;
        }

        public void Load()
        {
            // Parse Manifests
            string[] modFolders = Directory.GetDirectories(mModsDirectoryPath);
            if (modFolders == null)
            {
                return;
            }

            foreach (string modPath in modFolders)
            {
                string formatted = JsonHelper.NormalizeSystemPath(modPath);
                Module module = new Module(formatted);
                module.InitializeFromManifest();
                mModules.Add(module.Name, module);
            }

            foreach (Module module in mModules.Values)
            {
                module.LoadFiles();
            }

            foreach (Module module in mModules.Values)
            {
                module.PostLoadFixup();
            }
        }

        public void FilterAliasTree(TreeView treeView, string searchTerm)
        {
            treeView.BeginUpdate(); // blocks repainting tree till all objects loaded

            // filter
            treeView.Nodes.Clear();

            List<TreeNode> filteredNodes = new List<TreeNode>();
            foreach (Module module in mModules.Values)
            {
                TreeNode node = module.FilterAliasTree(searchTerm);
                if (node != null)
                {
                    filteredNodes.Add(node);
                }
            }

            treeView.Nodes.AddRange(filteredNodes.ToArray());

            // enables redrawing tree after all objects have been added
            treeView.EndUpdate();
        }

        // Returns an Object array with a map from alias to jsonfiledata and alias to modname
        public object[] FilterJsonByTerm(ListView listView, string filterTerm)
        {
            Dictionary<string, JsonFileData> aliasJsonMap = new Dictionary<string, JsonFileData>();
            Dictionary<string, string> aliasModNameMap = new Dictionary<string, string>();
            foreach (Module module in mModules.Values)
            {
                foreach (ModuleFile moduleFile in module.GetAliases())
                {
                    JsonFileData data = moduleFile.GetJsonFileDataByTerm(filterTerm);
                    if (data != null)
                    {
                        aliasJsonMap.Add(moduleFile.Name, data);
                        aliasModNameMap.Add(moduleFile.Name, module.Name);
                    }
                }
            }

            return new object[] { aliasJsonMap, aliasModNameMap };
        }

        public FileData GetSelectedFileData(TreeNode selected)
        {
            if (selected != null)
            {
                string fullPath = selected.FullPath;
                return GetSelectedFileData(fullPath);
            }

            return null;
        }

        public bool IsModuleFileSelected(TreeNode selected)
        {
            if (selected == null)
            {
                return false;
            }

            string[] path = selected.FullPath.Split('\\');
            if (path.Length != 2)
            {
                return false;
            }

            return true;
        }

        public FileData GetSelectedFileData(string selected)
        {
            string[] path = selected.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (path.Length <= 2)
            {
                return null;
            }

            Module module = mModules[path[0]];
            ModuleFile file = module.GetModuleFile(path[1], path[2]);
            if (file != null)
            {
                return file.GetFileData(path);
            }

            return null;
        }

        public ModuleFile GetModuleFile(string fullAlias)
        {
            int indexOfColon = fullAlias.IndexOf(':');
            string module = fullAlias.Substring(0, indexOfColon);
            string alias = fullAlias.Substring(indexOfColon + 1);
            Module mod = ModuleDataManager.GetInstance().GetMod(module);
            if (mod == null)
            {
                return null;
            }

            return mod.GetAliasFile(alias);
        }

        /// <summary>
        /// Attempts to resolve a reference.
        /// </summary>
        /// <param name="path">Reference that was given</param>
        /// <param name="parentDirectory">Parent directory, required to resolve relative paths.</param>
        /// <param name="moduleFile">The file that was found.</param>
        /// <returns><c>true</c> if the file was found, <c>false</c> otherwise.</returns>
        public bool TryGetModuleFile(string path, string parentDirectory, out ModuleFile moduleFile)
        {
            moduleFile = null;

            // Alias?
            if (path.Contains(":"))
            {
                moduleFile = this.GetModuleFile(path);
                return true;
            }
            else
            {
                var fileName = JsonHelper.GetFileFromFileJson(path, parentDirectory);

                // Is it a valid filename?
                if (!File.Exists(fileName))
                    return false; // Not a valid file => we don't stand a chance to begin with

                // Cut away the unrequired bits
                var simplifiedFileName = fileName.Replace(ModuleDataManager.GetInstance().ModsDirectoryPath, "").TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

                // Split it into mod/path within mod
                var parts = simplifiedFileName.Split(new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar }, 2);

                var mod = ModuleDataManager.GetInstance().GetMod(parts[0]);

                if (mod == null)
                    return false;

                // The file exists, but we can't say much beyond that.
                return true;

                ////// Get all aliases that match this file
                ////var aliases = mod.GetAliases().Where(alias => alias.ResolvedPath == fileName).ToList();
                ////return null;
            }
        }

        public ICollection<Module> GetAllModules()
        {
            return mModules.Values;
        }

        public Module GetMod(string modName)
        {
            if (!mModules.ContainsKey(modName))
            {
                return null;
            }

            return mModules[modName];
        }

        public string LocalizeString(string key)
        {
            string[] split = key.Split(':');
            string modName = "stonehearth";
            if (split.Length > 1)
            {
                modName = split[0];
                key = split[1];
            }

            Module mod = GetMod(modName);
            if (mod != null)
            {
                JToken token = mod.EnglishLocalizationJson.SelectToken(key);
                if (token != null)
                {
                    return token.ToString();
                }
            }

            return key;
        }

        public bool HasLocalizationKey(string key)
        {
            string[] split = key.Split(':');
            string modName = "stonehearth";
            if (split.Length > 1)
            {
                modName = split[0];
                key = split[1];
            }

            Module mod = GetMod(modName);
            if (mod != null)
            {
                JToken token = mod.EnglishLocalizationJson.SelectToken(key);
                if (token != null)
                {
                    return true;
                }
            }

            return false;
        }

        // Call to clone an alias. top level. nested clone calls should call the module directly.
        public bool ExecuteClone(ModuleFile module, CloneObjectParameters parameters, HashSet<string> unwantedItems)
        {
            return module.Clone(parameters, unwantedItems, true);
        }

        public bool ExecuteClone(FileData file, CloneObjectParameters parameters, HashSet<string> unwantedItems)
        {
            ModuleFile owningFile = (file as IModuleFileData).GetModuleFile();
            if (owningFile != null)
            {
                return ExecuteClone(owningFile, parameters, unwantedItems);
            }

            string newPath = parameters.TransformParameter(file.Path);
            return file.Clone(newPath, parameters, unwantedItems, true);
        }

        public HashSet<string> PreviewCloneDependencies(ModuleFile module, CloneObjectParameters cloneParameters)
        {
            HashSet<string> alreadyCloned = new HashSet<string>();
            module.Clone(cloneParameters, alreadyCloned, false);
            return alreadyCloned;
        }

        public HashSet<string> PreviewCloneDependencies(FileData file, CloneObjectParameters cloneParameters)
        {
            ModuleFile owningFile = (file as IModuleFileData).GetModuleFile();
            if (owningFile != null)
            {
                return PreviewCloneDependencies(owningFile, cloneParameters);
            }

            HashSet<string> alreadyCloned = new HashSet<string>();
            string newPath = cloneParameters.TransformParameter(file.Path);

            file.Clone(newPath, cloneParameters, alreadyCloned, false);
            return alreadyCloned;
        }

        public int GetAverageMaterialCost(string material)
        {
            if (mAverageMaterialCost.ContainsKey(material))
            {
                return mAverageMaterialCost[material];
            }

            int sumCost = 0;
            int numItems = 0;
            string[] split = material.Split(' ');
            foreach (Module mod in ModuleDataManager.GetInstance().GetAllModules())
            {
                foreach (ModuleFile file in mod.GetAliases())
                {
                    JsonFileData data = file.FileData as JsonFileData;
                    if (data == null)
                    {
                        continue;
                    }

                    int netWorth = data.NetWorth;
                    if (netWorth <= 0)
                    {
                        continue;
                    }

                    JToken tags = data.Json.SelectToken("components.stonehearth:material.tags");
                    if (tags != null)
                    {
                        string tagString = tags.ToString();
                        string[] currentTagSplit = tagString.Split(' ');
                        HashSet<string> currentTagSet = new HashSet<string>(currentTagSplit);
                        bool isMaterial = true;
                        foreach (string tag in split)
                        {
                            if (!currentTagSet.Contains(tag))
                            {
                                isMaterial = false;
                                break;
                            }
                        }

                        if (isMaterial)
                        {
                            numItems++;
                            sumCost = sumCost + netWorth;
                        }
                    }
                }
            }

            if (numItems > 0)
            {
                int averageCost = sumCost / numItems;
                mAverageMaterialCost[material] = averageCost;
                return averageCost;
            }

            return 0;
        }

        public void Dispose()
        {
            foreach (Module module in mModules.Values)
            {
                module.Dispose();
            }

            mModules.Clear();
            mFilesWithErrors.Clear();
            mAverageMaterialCost.Clear();
        }
    }
}
