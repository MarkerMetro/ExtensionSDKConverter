Reference Extension SDKs Locally (ExtensionSDKConverter)
=====================

VS Extension that converts SDKs to references by copying the extension artifacts into a folder and updating the project SDK reference path. Allows SDKs to be checked in without requiring VSIX install

## Why?
VSIX distributed extensions are installed into Visual Studio therefore requiring each developer to have the same version of the extension installed. If one developer upgrades then the build will break for everyone else until they upgrade. It also means that automated builds on a build server will require constant maintenance to install the current version of the extensions (not to mention conflicting versions on different build definitions).

#How?
An [excellent article by Oren Novotny](http://novotny.org/blog/how-to-use-extension-sdks-per-project) outlines the solution, to make a change to the project file instructing Visual Studio to look in a local folder in the solution for SDKs first before checking the default machine location.

The folder needs a particular folder structure and naming convention for the above to work.

#Guide
Follow the step below to use the extension.

1. Install the extension "Reference Extension SDKs locally" from the Visual Studio Gallery. (http://visualstudiogallery.msdn.microsoft.com/1f027247-1e01-4ec6-8f5b-70dabb375217)
2. In your project, right click on the extension and choose the command of the same name.
3. You may be prompted for the location to save the SDKs. Choose/create a folder beside your solution file e.g. libs
4. **Make sure you checkin/commit everything in this folder to your source control repository.** Note that your VCS (e.g. GIT) may be configured to ignore certain path patterns e.g. bin/release/debug so some files in here could get ignored. 

You should notice that the reference path in your project will point to your new local folder.

Now all the developers in your team will pick up the same version of the SDK from the local copy, as will the build server.

If you wish to upgrade, just delete the files from the libs folder, and repeat the above process.

#What if something goes wrong?
...or What have you done to my lovely project?

If you need undo the changes that have been made to your project it's simple.

* [Edit your project file](http://msdn.microsoft.com/en-us/library/ms171487(v=vs.90).aspx) and remove the section that looks like this

```xml
  <PropertyGroup>
       <SDKReferenceDirectoryRoot>$(SolutionDir)\.\libs;$(LocalAppData)\Microsoft SDKs;$(MSBuildProgramFiles32)\Microsoft SDKs</SDKReferenceDirectoryRoot>
  </PropertyGroup>
```

* Remove the folder containing the SDKs e.g. labs

Now everything should be back to the way we found it.


