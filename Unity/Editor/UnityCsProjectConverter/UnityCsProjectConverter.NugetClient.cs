/** Legacy-to-SDK-Style .csproj Converter for Unity
 ** (c) 2024 https://github.com/sator-imaging
 ** Licensed under the MIT License
 */

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

#nullable enable

namespace SatorImaging.Csproj.Sdk
{
    partial class UnityCsProjectConverter
    {
        internal static class NugetClient
        {
            readonly static string NUGET_EP = @"https://api.nuget.org/v3-flatcontainer/csproj.sdk.void/index.json";
            readonly static string MIME_JSON = @"application/json";
            readonly static string PREF_LAST_FETCH_TIME = nameof(SatorImaging) + nameof(Csproj) + nameof(UnityCsProjectConverter) + nameof(PREF_LAST_FETCH_TIME);
            readonly static DateTime FETCH_TIME_EPOCH = new(2024, 1, 1);  // TODO: overflows 68 years later!

            // json representation
            [Serializable] sealed class NugetPayload { public string[]? versions; }

            internal static string? GetVoidSdkVersionFromNugetOrgThread()
            {
                // NOTE: don't save last fetch time in prefs!! no worth to share each user fetch time on git!!
                var lastFetchTimeSecsFromCustomEpoch = EditorPrefs.GetInt(PREF_LAST_FETCH_TIME, 0);

                string? foundLatestVersion = null;

                // don't fetch nuget.org repeatedly
                var prefs = Prefs.Instance;
                var elapsedTimeFromLastFetch = DateTime.UtcNow - FETCH_TIME_EPOCH.AddSeconds(lastFetchTimeSecsFromCustomEpoch);
                if (elapsedTimeFromLastFetch.TotalDays < 1)
                {
                    //UnityEngine.Debug.Log("[NuGet] elapsed time from last fetch < 1 day: " + elapsedTimeFromLastFetch);

                    foundLatestVersion = prefs.LatestVoidSdkVersion;
                    goto VALIDATE_AND_RETURN;
                }

                EditorPrefs.SetInt(PREF_LAST_FETCH_TIME, Convert.ToInt32((DateTime.UtcNow - FETCH_TIME_EPOCH).TotalSeconds));


                UnityEngine.Debug.Log("[NuGet] fetching nuget.org for package information...: " + SDK_VOID_NAME_SLASH);

                var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    ThreadPool.QueueUserWorkItem(async static (tcs) =>
                    {
                        //threadPool
                        {
                            {
                                int timeoutMillisecs = Prefs.Instance.NugetOrgTimeoutMillis;

                                using var cts = new CancellationTokenSource(timeoutMillisecs);

                                using var client = new HttpClient();
                                client.DefaultRequestHeaders.Accept.Clear();
                                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(MIME_JSON));

                                try
                                {
                                    var response = await client.GetAsync(NUGET_EP, cts.Token);
                                    if (response.IsSuccessStatusCode)
                                    {
                                        var json = await response.Content.ReadAsStringAsync();
                                        tcs.SetResult(json);
                                    }
                                }
                                catch (Exception exc)
                                {
                                    tcs.SetException(exc);
                                }

                                tcs.SetException(new Exception("unhandled error"));
                            }
                        }
                    },
                    tcs, false);


                    // this will throw when thread task is failed
                    var json = tcs.Task.ConfigureAwait(false).GetAwaiter().GetResult();
                    var result = JsonUtility.FromJson<NugetPayload>(json);

                    foundLatestVersion = result.versions?[result.versions.Length - 1];
                    prefs.LatestVoidSdkVersion = foundLatestVersion;

                    UnityEngine.Debug.Log("[NuGet] latest version found: " + SDK_VOID_NAME_SLASH + foundLatestVersion);

                    goto VALIDATE_AND_RETURN;
                }
                catch (Exception exc)
                {
                    UnityEngine.Debug.LogError("[NuGet] operation timed out or api v3 is not available: " + exc);
                }

            VALIDATE_AND_RETURN:
                if (string.IsNullOrWhiteSpace(foundLatestVersion))
                {
                    prefs.LatestVoidSdkVersion = null;
                    return null;
                }
                return foundLatestVersion;
            }
        }

    }
}
