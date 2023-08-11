using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MaxMind.Db;
using Rumble.Platform.Common.Utilities;
using Rumble.Platform.Common.Web;
using Rumble.Platform.Common.Models;
using Rumble.Platform.Common.Services;
using Rumble.Platform.Data;
using Owner = RCL.Logging.Owner;
using Timer = System.Timers.Timer;

namespace Rumble.Platform.Common.Interop;

public static class MaxMind
{
    public const string FALLBACK_FILENAME = "GeoIP2-Country.mmdb";
    public const string FALLBACK_BUCKET = "rumble-geoip-us-east-1";
    
    internal static string Filename { get; private set; }
    internal static string BucketName { get; private set; }
    
    private static bool DownloadComplete { get; set; }

    private static void Initialize()
    {
        try
        {
            Filename ??= DynamicConfig.Instance?.Optional<string>("Maxmind_Bucket") ?? FALLBACK_BUCKET;
            BucketName ??= DynamicConfig.Instance?.Optional<string>("Maxmind_Filename") ?? FALLBACK_FILENAME;
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Could not initialize AWS variables for Maxmind interop", exception: e);
            Filename = FALLBACK_FILENAME;
            BucketName = FALLBACK_BUCKET;
        }

        if (DownloadComplete)
            return;

        Download();
    }
    
    public static GeoIPData Lookup(string ipAddress)
    {
        Initialize();
        
        try
        {
            IPAddress ip = IPAddress.Parse(ipAddress); 

            using Reader reader = new Reader(Filename); 
            Dictionary<string, object> dict = reader.Find<Dictionary<string, object>>(ip);
            RumbleJson data = RumbleJson.FromDictionary(dict);
            return new GeoIPData(ipAddress, data);
        }
        catch (Exception e)
        {
            Log.Error(Owner.Will, "Unable to retrieve GeoIP data.", data: new
            {
                IpAddress = ipAddress,
                Help = "This likely means the MaxMind DB failed to download, or does not have this IP address listing in it."
            }, exception: e);
            return new GeoIPData(ipAddress);
        }
    }
    
    private static Timer RetryTimer { get; set; }

    /// <summary>
    /// Attempts to download the latest Maxmind DB file.  This is critical to making sure we don't have null/empty
    /// results for IP data.  If the download fails, the service will re-attempt it up to 5 additional times with a delay.
    /// If the download fails outright, platform will use a local fallback database from the repo.
    /// </summary>
    /// <param name="retries">The number of times to retry to download the maxmind database from S3.</param>
    /// <returns>True if the download was successful, otherwise false.</returns>
    public static bool Download(int retries = 5)
    {
        try
        {
            BasicAWSCredentials credentials = new BasicAWSCredentials(
                accessKey: PlatformEnvironment.Require<string>("AWS_SES_ACCESS_KEY"),
                secretKey: PlatformEnvironment.Require<string>("AWS_SES_SECRET_KEY")
            );
            AmazonS3Client client = new AmazonS3Client(credentials, region: RegionEndpoint.USEast1);

            GetObjectRequest request = new GetObjectRequest
            {
                BucketName = DynamicConfig.Instance?.Optional<string>("Maxmind_Bucket") ?? FALLBACK_BUCKET,
                Key = DynamicConfig.Instance?.Optional<string>("Maxmind_Filename") ?? FALLBACK_FILENAME
            };

            Task<GetObjectResponse> task = client.GetObjectAsync(request);
            task.Wait();
            
            GetObjectResponse response = task.Result;
            response.WriteResponseStreamToFileAsync(Filename, append: false, CancellationToken.None).Wait();
            DownloadComplete = true;
            Log.Local(Owner.Will, "Downloaded latest MaxMind DB.");
            RetryTimer?.Dispose();
            return true;
        }
        catch (Exception e)
        {
            if (retries == 0)
            {
                Log.Error(Owner.Will, "Unable to download latest MaxMind database; it may have been moved or is unavailable to platform common.", data: new
                {
                    Help = "A fallback local database will be used, but IP address lookups may fail, resulting in AQ telemetry codes."
                }, exception: e);
                DownloadComplete = true;
                return false;
            }

            Log.Error(Owner.Will, "Unable to download latest MaxMind database; retrying...", data: new
            {
                Help = "A fallback local database will be used temporarily.",
                RetriesRemaining = retries
            }, exception: e);

            if (RetryTimer != null)
                return false;
            
            Log.Local(Owner.Will, "Creating a retry timer");
            RetryTimer = new Timer(interval: 300_000); // five minutes
            RetryTimer.Elapsed += (_, _) =>
            {
                RetryTimer.Stop();
                if (Download(--retries) || retries == 0)
                {
                    RetryTimer.Dispose();
                    return;
                }
                RetryTimer.Start();
            };
            RetryTimer.Start();

            return false;
        }

    }
}