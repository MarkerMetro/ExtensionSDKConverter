using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ComponentModel.Design;
using System.Windows.Forms;
using System.Xml.Linq;
using EnvDTE;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell;
using VSLangProj110;
using Project = Microsoft.Build.Evaluation.Project;

namespace MarkerMetro.ExtensionSDKConverter
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExistsAndNotBuildingAndNotDebugging)]
    [Guid(GuidList.guidExtensionSDKConverterPkgString)]
    public sealed class ExtensionSDKConverterPackage : Package
    {
        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public ExtensionSDKConverterPackage()
        {
            Debug.WriteLine(string.Format(CultureInfo.CurrentCulture, "Entering constructor for: {0}", this.ToString()));
        }



        /////////////////////////////////////////////////////////////////////////////
        // Overridden Package Implementation
        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            Debug.WriteLine (string.Format(CultureInfo.CurrentCulture, "Entering Initialize() of: {0}", this.ToString()));
            base.Initialize();

            // Add our command handlers for menu (commands must exist in the .vsct file)
            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if ( null != mcs )
            {
                // Create the command for the menu item.
                CommandID menuCommandID = new CommandID(GuidList.guidExtensionSDKConverterCmdSet, (int)PkgCmdIDList.cmdidConvertSdkToReference);
                OleMenuCommand menuItem = new OleMenuCommand(MenuItemCallback, null, OnMenuItemQueryStatus, menuCommandID);
                mcs.AddCommand( menuItem );
            }
        }
        #endregion

        /// <summary>
        /// This function is the callback used to execute a command when the a menu item is clicked.
        /// See the Initialize method to see how the menu item is associated to this function using
        /// the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void MenuItemCallback(object sender, EventArgs e)
        {
            var reference = GetSelectedReference();
            if (reference == null || reference.RefType != 4)
            {
                return;
            }

            ProjectCollection engine = ProjectCollection.GlobalProjectCollection;
            Project project = engine.GetLoadedProjects(reference.ContainingProject.FullName).FirstOrDefault();

            if (project == null)
            {
                ShowError("There was an error enumerating the projects");
                return;
            }

            var dte = (DTE)GetService(typeof(DTE));
            bool isProjectFileChanged = false;
            bool hasSdkPathOverride = false;
            var solutionFolder = Path.GetDirectoryName(dte.Solution.FullName);
            string sdkLibsFolderPath = null;

            var sdkRootProperty = project.GetProperty("SDKReferenceDirectoryRoot");

            // The imported property is the global one so ignore it

            if (sdkRootProperty != null && !sdkRootProperty.IsImported)
            {
                // We already have a property override
                hasSdkPathOverride = true;
                var unevaluatedPaths = sdkRootProperty.UnevaluatedValue.Split(';');
                var evaluatedPaths = sdkRootProperty.EvaluatedValue.Split(';');
                string unevaluatedPath = null;
                string evaluatedPath = null;
                var index = 0;
                foreach (var p in unevaluatedPaths)
                {
                    if (p.ToLower().Contains("$(SolutionDir)".ToLower()))
                    {
                        unevaluatedPath = p;
                        evaluatedPath = evaluatedPaths[index];
                        break;
                    }
                    index++;
                }
                if (unevaluatedPath != null)
                {
                    // We have multiple paths declared
                    sdkLibsFolderPath = evaluatedPath;
                }
            }

            // Parse the SDKManifest.xml file
            var manifest = GetSdkManifest(reference);
            if (manifest == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(sdkLibsFolderPath))
            {
                
                var dialog = new FolderBrowserDialog
                {
                    Description =
                        "Pick / Create a folder in which to store the SDK. e.g. libs folder beside the solution file",
                    RootFolder = Environment.SpecialFolder.MyComputer,
                    SelectedPath = solutionFolder,
                    ShowNewFolderButton = true
                };

                var folderPickerDialogResult = dialog.ShowDialog();
                if (folderPickerDialogResult != DialogResult.OK)
                {
                    return;
                }
                sdkLibsFolderPath = dialog.SelectedPath;
            }

            // Check if we already have a copy of this SDK
            var newReferencePath = Path.Combine(sdkLibsFolderPath, manifest.TargetPlatformIdentifier, manifest.TargetPlatformVersion, "ExtensionSDKs", manifest.SdkName, manifest.SdkVersion);
            var sdkOverrideAlreadyContainsPath = hasSdkPathOverride && sdkRootProperty.EvaluatedValue.ToLower().Split(';').Select(s => s.Trim('\\')).Contains(sdkLibsFolderPath.ToLower().Trim('\\'));

            if (Directory.Exists(newReferencePath) && sdkOverrideAlreadyContainsPath)
            {
                ShowInfo(string.Format("The SDK '{0}' Version '{1}' has already been imported into the solution. Solution explorer should show a path local to the solution for this SDK content. If not refresh solution explorer", manifest.SdkName, manifest.SdkVersion));
                return;
            }

            // Ensure the target directry exists
            if (!Directory.Exists(newReferencePath))
            {
                Directory.CreateDirectory(newReferencePath);

                // Copy the files to the SDK folder
                CopyDirectoryContents(reference.Path, newReferencePath);
            }
            
            // Make changes to the project file if necessary
            if (!hasSdkPathOverride || !sdkOverrideAlreadyContainsPath)
            {
                var propGroup = project.Xml.AddPropertyGroup();
                
                var relativeSolutionPath = RelativePathCreator.GetRelativePath(solutionFolder, sdkLibsFolderPath);
                if (!string.IsNullOrEmpty(relativeSolutionPath) && !relativeSolutionPath.StartsWith(@"\"))
                {
                    relativeSolutionPath = string.Concat(@"\", relativeSolutionPath);
                }
                var existingValue = sdkRootProperty == null ? "" : sdkRootProperty.UnevaluatedValue;
                propGroup.AddProperty("SDKReferenceDirectoryRoot", string.Format("$(SolutionDir){0};{1}", relativeSolutionPath, existingValue));

                isProjectFileChanged = true;
            }


            // Modify project file using project.
            if (isProjectFileChanged)
            {
                try
                {
                    project.Save(reference.ContainingProject.FullName);

                    ShowInfo(
                        "The extension has been added to the folder you specified. You may be asked to reload your project, after which the path for the reference should be the same path you chose earlier");
                }
                catch (Exception exception)
                {
                    ShowError(string.Format("There was an error saving the project file: {0}", exception.Message));
                }
            }
            else
            {
                ShowInfo("The extension has been added to the folder you specified. You may need to refresh solution explorer, after which the path for the reference should be the same path you chose earlier");
            }
        }

        private void CopyDirectoryContents(string source, string destination)
        {
            if (!source.EndsWith("\\"))
            {
                source = source + "\\";
            }
            if (!destination.EndsWith("\\"))
            {
                destination = destination + "\\";
            }
            //Now Create all of the directories
            foreach (var dirPath in Directory.GetDirectories(source, "*",
                SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(source, destination));

            //Copy all the files
            foreach (var newPath in Directory.GetFiles(source, "*.*",
                SearchOption.AllDirectories))
                File.Copy(newPath, newPath.Replace(source, destination));
        }

        private void ShowError(string message)
        {
            IVsUIShell uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
            Guid clsid = Guid.Empty;
            int result;
            uiShell.ShowMessageBox(0,
                       ref clsid,
                       "Extension SDK Converter",
                       string.Format(CultureInfo.CurrentCulture, message),
                       string.Empty,
                       0,
                       OLEMSGBUTTON.OLEMSGBUTTON_OK,
                       OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                       OLEMSGICON.OLEMSGICON_CRITICAL,
                       0,        // false
                       out result);
        }

        private void ShowInfo(string message)
        {
            IVsUIShell uiShell = (IVsUIShell)GetService(typeof(SVsUIShell));
            Guid clsid = Guid.Empty;
            int result;
            uiShell.ShowMessageBox(0,
                       ref clsid,
                       "Extension SDK Converter",
                       string.Format(CultureInfo.CurrentCulture, message),
                       string.Empty,
                       0,
                       OLEMSGBUTTON.OLEMSGBUTTON_OK,
                       OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                       OLEMSGICON.OLEMSGICON_INFO,
                       0,        // false
                       out result);
        }

        private void OnMenuItemQueryStatus(object sender, EventArgs e)
        {
            OleMenuCommand menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                var reference = GetSelectedReference();
                var isExtensionSdk = reference != null && reference.RefType == 4;
                if (!isExtensionSdk)
                {
                    // Exit early if the reference is not an ExtensionSDK
                    menuCommand.Visible = false;
                    return;
                }

                var dte = (DTE)GetService(typeof(DTE));
                var solutionDir = Path.GetDirectoryName(dte.Solution.FullName);

                // Only visible if the reference is not already beneath our solution folder
                menuCommand.Visible = solutionDir == null || !reference.Path.ToLower().Contains(solutionDir.ToLower());
            }
        }

        private Reference5 GetSelectedReference()
        {
            IntPtr hierarchyPtr = IntPtr.Zero, selectionContainerPtr = IntPtr.Zero;
            try
            {
                
                uint projectItemId;
                IVsMultiItemSelect mis;
                IVsMonitorSelection monitorSelection =
                    (IVsMonitorSelection) GetGlobalService(typeof (SVsShellMonitorSelection));
                monitorSelection.GetCurrentSelection(out hierarchyPtr, out projectItemId, out mis,
                    out selectionContainerPtr);

                IVsHierarchy hierarchy =
                    Marshal.GetTypedObjectForIUnknown(hierarchyPtr, typeof (IVsHierarchy)) as IVsHierarchy;

                if (hierarchy != null)
                {
                    object referenceObject;
                    hierarchy.GetProperty(projectItemId, (int) __VSHPROPID.VSHPROPID_ExtObject, out referenceObject);

                    var reference = (Reference5) referenceObject;

                    return reference;
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Marshal.Release(hierarchyPtr);
                Marshal.Release(selectionContainerPtr);
            }

        }

        private SdkInstallationInfo GetSdkManifest(Reference5 reference)
        {
            if (!Directory.Exists(reference.Path) || !File.Exists(Path.Combine(reference.Path, "extension.vsixmanifest")))
            {
                return null;
            }

            var manifestDoc = XDocument.Load(Path.Combine(reference.Path, "extension.vsixmanifest"));
            var installationTarget = manifestDoc.Descendants(XName.Get("InstallationTarget", "http://schemas.microsoft.com/developer/vsx-schema/2011")).FirstOrDefault();
            if (installationTarget == null)
            {
                return null;
            }
            
            return new SdkInstallationInfo
            {
                Id = installationTarget.Attribute("Id").Value,
                TargetPlatformIdentifier = installationTarget.Attribute("TargetPlatformIdentifier").Value,
                TargetPlatformVersion = installationTarget.Attribute("TargetPlatformVersion").Value,
                SdkName = installationTarget.Attribute("SdkName").Value,
                SdkVersion = installationTarget.Attribute("SdkVersion").Value,
            };
        }

        private class SdkInstallationInfo
        {
            public string Id { get; set; }
            public string TargetPlatformIdentifier { get; set; }
            public string TargetPlatformVersion { get; set; }
            public string SdkName { get; set; }
            public string SdkVersion { get; set; }
        }
    }
}
