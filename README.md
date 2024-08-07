`Csproj.Sdk` is msbuild SDK collection for C# project (`.csproj` file) to migrate legacy-style project to newer SDK-style format.

[![Csproj.Sdk.Void](https://img.shields.io/nuget/v/csproj.sdk.void?label=Csproj.Sdk.Void)](https://www.nuget.org/packages/Csproj.Sdk.Void/)



# How to Migrate `.csproj` to SDK-Style

For example, use `Csproj.Sdk.Void` as an SDK for your `.csproj`.

```xml
<Project Sdk="Csproj.Sdk.Void/1.1.0">
    ...
    ... original file contents except for <Project> tag ...
    ...
</Project>
```

> [!NOTE]
> Version prefix (`/1.1.0`) must be specified to activate internal nuget resolver.

This will make project sdk-style and allow setting nullability for whole project by adding `<Nullable>enable</Nullable>` to .csproj.
Surprisingly, nullability setting is not recognized by Visual Studio if `Project` tag doesn't have `Sdk` attribute!!

See: [\<Nullable> has no effect in old-style csproj](https://github.com/dotnet/project-system/issues/5551)



# Available SDKs

## `Csproj.Sdk.Void`

[src/Csproj.Sdk.Void/](src/Csproj.Sdk.Void/)

This SDK will not do anything, is just for converting legacy-style project to sdk-style.


## `Csproj.Sdk.Unity.VisualStudio`

T.B.D.



# Unity Integration

To migrate Unity project to sdk-style format, see [Unity/README.md](Unity/README.md)

*Note*: Unity's Visual Studio Editor package changelog describes that adding support generation of SDK-style project, but unfortunately, it still generates legacy-style format with some changes such as project header updated to `<Project ToolsVersion="Current">`. (actually it is sdk-importing-legacy-style project)

And also sdk-importing-style format generation is only performed when External Script Editor is set to `VS Code`.
Unity is still generating legacy-format for `Visual Studio` environment.



&nbsp;  
&nbsp;  

# Devnote

## `MSBuildSdk` Package Development Reference

There are no well-describing documents about `MSBuildSdk` package development. See the following source code for reference instead.

MSBuild SDKs
https://github.com/microsoft/MSBuildSdks/tree/main/src
