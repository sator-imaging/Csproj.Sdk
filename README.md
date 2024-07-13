# What's `Csproj.Sdk`?

`Csproj.Sdk` is msbuild's SDK package that can be used for `.csproj` file to migrate legacy-style project to newer SDK-style format.



# How to Migrate `.csproj` to SDK-Style

## Visual Studio

To migrate, use `Csproj.Sdk.Void` as an SDK for your `.csproj`.

```xml
<Project Sdk="Csproj.Sdk.Void/1.0.0">
    ...
    ... original file contents ...
    ...
</Project>
```

> [!NOTE]
> Version prefix (`/1.0.0`) must be specified to activate internal nuget resolver.

This will allow setting nullability for whole project by adding `<Nullable>enable</Nullable>` to .csproj.
Surprisingly, nullability setting is not recognized by Visual Studio if `Project` tag doesn't have `Sdk` attribute!!

See: [\<Nullable> has no effect in old-style csproj](https://github.com/dotnet/project-system/issues/5551)



# Unity Integration

See [Unity/README.md](Unity/README.md)

Note that Unity's Visual Studio Editor package changelog describes that adding support generation of SDK-style project, but unfortunately, it still generates legacy-style format with some changes such as project header updated to `<Project ToolsVersion="Current">`. (actually it is sdk-importing-legacy-style project)

And also, sdk-importing-style format generation is only performed when External Script Editor is set to `VS Code`.
Unity is still generating legacy-format for `Visual Studio` environment.



&nbsp;  
&nbsp;  

# Devnote

## `MSBuildSdk` Package Development Reference

There are no well-describing documents about `MSBuildSdk` package development. See the following source code for reference instead.

MSBuild SDKs
https://github.com/microsoft/MSBuildSdks/tree/main/src
