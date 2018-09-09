﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using InstagramApiSharp.Classes;
using InstagramApiSharp.Classes.Android.DeviceInfo;
using InstagramApiSharp.Classes.Models;
using InstagramApiSharp.Classes.ResponseWrappers;
using InstagramApiSharp.Converters;
using InstagramApiSharp.Converters.Json;
using InstagramApiSharp.Helpers;
using InstagramApiSharp.Logger;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InstagramApiSharp.API.Processors
{
    internal class MediaProcessor : IMediaProcessor
    {
        private readonly AndroidDevice _deviceInfo;
        private readonly IHttpRequestProcessor _httpRequestProcessor;
        private readonly IInstaLogger _logger;
        private readonly UserSessionData _user;
        private readonly UserAuthValidate _userAuthValidate;
        private readonly InstaApi _instaApi;
        public MediaProcessor(AndroidDevice deviceInfo, UserSessionData user,
            IHttpRequestProcessor httpRequestProcessor, IInstaLogger logger,
            UserAuthValidate userAuthValidate, InstaApi instaApi)
        {
            _deviceInfo = deviceInfo;
            _user = user;
            _httpRequestProcessor = httpRequestProcessor;
            _logger = logger;
            _userAuthValidate = userAuthValidate;
            _instaApi = instaApi;
        }
        /// <summary>
        ///     Get media ID from an url (got from "share link")
        /// </summary>
        /// <param name="uri">Uri to get media ID</param>
        /// <returns>Media ID</returns>
        public async Task<IResult<string>> GetMediaIdFromUrlAsync(Uri uri)
        {
            try
            {
                var collectionUri = UriCreator.GetMediaIdFromUrlUri(uri);
                var request = HttpHelper.GetDefaultRequest(HttpMethod.Get, collectionUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<string>(response, json);

                var data = JsonConvert.DeserializeObject<InstaOembedUrlResponse>(json);
                return Result.Success(data.MediaId);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<string>(exception);
            }
        }
        /// <summary>
        ///     Delete a media (photo or video)
        /// </summary>
        /// <param name="mediaId">The media ID</param>
        /// <param name="mediaType">The type of the media</param>
        /// <returns>Return true if the media is deleted</returns>
        public async Task<IResult<bool>> DeleteMediaAsync(string mediaId, InstaMediaType mediaType)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var deleteMediaUri = UriCreator.GetDeleteMediaUri(mediaId, mediaType);

                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"media_id", mediaId}
                };

                var request =
                    HttpHelper.GetSignedRequest(HttpMethod.Get, deleteMediaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<bool>(response, json);

                var deletedResponse = JsonConvert.DeserializeObject<DeleteResponse>(json);
                return Result.Success(deletedResponse.IsDeleted);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }
        /// <summary>
        ///     Edit the caption of the media (photo/video)
        /// </summary>
        /// <param name="mediaId">The media ID</param>
        /// <param name="caption">The new caption</param>
        /// <returns>Return true if everything is ok</returns>
        public async Task<IResult<bool>> EditMediaAsync(string mediaId, string caption)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var editMediaUri = UriCreator.GetEditMediaUri(mediaId);

                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"caption_text", caption}
                };

                var request = HttpHelper.GetSignedRequest(HttpMethod.Get, editMediaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode == HttpStatusCode.OK)
                    return Result.Success(true);
                var error = JsonConvert.DeserializeObject<BadStatusResponse>(json);
                return Result.Fail(error.Message, false);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }
        /// <summary>
        ///     Upload video
        /// </summary>
        /// <param name="video">Video and thumbnail to upload</param>
        /// <param name="caption">Caption</param>
        public async Task<IResult<InstaMedia>> UploadVideoAsync(InstaVideoUpload video, string caption)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetUploadVideoUri();
                var uploadId = ApiRequestMessage.GenerateUploadId();
                var requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent("2"), "\"media_type\""},
                    {new StringContent(uploadId), "\"upload_id\""},
                    {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                        "\"image_compression\""
                    }
                };

                var request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                var videoResponse = JsonConvert.DeserializeObject<VideoUploadJobResponse>(json);
                if (videoResponse == null)
                    return Result.Fail<InstaMedia>("Failed to get response from instagram video upload endpoint");

                byte[] fileBytes;
                if (video.Video.VideoBytes == null)
                    fileBytes = File.ReadAllBytes(video.Video.Uri);
                else
                    fileBytes = video.Video.VideoBytes;
                var first = videoResponse.VideoUploadUrls[0];
                instaUri = new Uri(Uri.EscapeUriString(first.Url));


                requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                        "\"image_compression\""
                    }
                };


                var videoContent = new ByteArrayContent(fileBytes);
                videoContent.Headers.Add("Content-Transfer-Encoding", "binary");
                videoContent.Headers.Add("Content-Type", "application/octet-stream");
                videoContent.Headers.Add("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(video.Video.Uri)}\"");
                requestContent.Add(videoContent);
                request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                request.Headers.Host = "upload.instagram.com";
                request.Headers.Add("Cookie2", "$Version=1");
                request.Headers.Add("Session-ID", uploadId);
                request.Headers.Add("job", first.Job);
                response = await _httpRequestProcessor.SendAsync(request);
                json = await response.Content.ReadAsStringAsync();

                await UploadVideoThumbnailAsync(video.VideoThumbnail, uploadId);

                return await ConfigureVideoAsync(video.Video, uploadId, caption);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        private async Task<IResult<bool>> UploadVideoThumbnailAsync(InstaImage image, string uploadId)
        {
            try
            {
                var instaUri = UriCreator.GetUploadPhotoUri();
                var requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent(uploadId), "\"upload_id\""},
                    {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                        "\"image_compression\""
                    }
                };
                byte[] fileBytes;
                if (image.ImageBytes == null)
                    fileBytes = File.ReadAllBytes(image.Uri);
                else
                    fileBytes = image.ImageBytes;

                var imageContent = new ByteArrayContent(fileBytes);
                imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                imageContent.Headers.Add("Content-Type", "application/octet-stream");
                requestContent.Add(imageContent, "photo", $"pending_media_{uploadId}.jpg");
                var request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var imgResp = JsonConvert.DeserializeObject<ImageThumbnailResponse>(json);
                if (imgResp.Status.ToLower() == "ok")
                    return Result.Success(true);
                else
                    return Result.Fail<bool>("Could not upload thumbnail");
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }

        private async Task<IResult<InstaMedia>> ConfigureVideoAsync(InstaVideo video, string uploadId, string caption)
        {
            try
            {
                var instaUri = UriCreator.GetMediaConfigureUri();
                var data = new JObject
                {
                    {"caption", caption},
                    {"upload_id", uploadId},
                    {"source_type", "3"},
                    {"camera_position", "unknown"},
                    {
                        "extra", new JObject
                        {
                            {"source_width", video.Width},
                            {"source_height", video.Height}
                        }
                    },
                    {
                        "clips", new JArray{
                            new JObject
                            {
                                {"length", 10.0},
                                {"creation_date", DateTime.Now.ToString("yyyy-dd-MMTh:mm:ss-0fff")},
                                {"source_type", "3"},
                                {"camera_position", "back"}
                            }
                        }
                    },
                    {"poster_frame_index", 0},
                    {"audio_muted", false},
                    {"filter_type", "0"},
                    {"video_result", "deprecated"},
                    {"_csrftoken", _user.CsrfToken},
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.UserName}
                };

                var request = HttpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Host = "i.instagram.com";
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return Result.UnExpectedResponse<InstaMedia>(response, json);

                var success = await ExposeVideoAsync(uploadId);
                if (success.Succeeded)
                    return Result.Success(success.Value);
                else
                    return Result.Fail<InstaMedia>("Cannot expose media");
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        private async Task<IResult<InstaMedia>> ExposeVideoAsync(string uploadId)
        {
            try
            {
                var instaUri = UriCreator.GetMediaConfigureUri();
                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"experiment", "ig_android_profile_contextual_feed"},
                    {"id", _user.LoggedInUser.Pk},
                    {"upload_id", uploadId},

                };

                var request = HttpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                request.Headers.Host = "i.instagram.com";
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var jObject = JsonConvert.DeserializeObject<ImageThumbnailResponse>(json);

                if (jObject.Status.ToLower() == "ok")
                {
                    var mediaResponse = JsonConvert.DeserializeObject<InstaMediaItemResponse>(json, 
                        new InstaMediaDataConverter());
                    var converter = ConvertersFabric.Instance.GetSingleMediaConverter(mediaResponse);
                    return Result.Success(converter.Convert());
                }
                else
                    return Result.Fail<InstaMedia>(jObject.Status);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }
        /// <summary>
        ///     Upload photo
        /// </summary>
        /// <param name="image">Photo to upload</param>
        /// <param name="caption">Caption</param>
        public async Task<IResult<InstaMedia>> UploadPhotoAsync(InstaImage image, string caption)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var instaUri = UriCreator.GetUploadPhotoUri();
                var uploadId = ApiRequestMessage.GenerateUploadId();
                var requestContent = new MultipartFormDataContent(uploadId)
                {
                    {new StringContent(uploadId), "\"upload_id\""},
                    {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                    {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                    {
                        new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                        "\"image_compression\""
                    }
                };
                byte[] fileBytes;
                if (image.ImageBytes == null)
                    fileBytes = File.ReadAllBytes(image.Uri);
                else
                    fileBytes = image.ImageBytes;
                var imageContent = new ByteArrayContent(fileBytes);
                imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                imageContent.Headers.Add("Content-Type", "application/octet-stream");
                requestContent.Add(imageContent, "photo", $"pending_media_{ApiRequestMessage.GenerateUploadId()}.jpg");
                var request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                request.Content = requestContent;
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return await ConfigurePhotoAsync(image, uploadId, caption);
                return Result.UnExpectedResponse<InstaMedia>(response, json);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }
        /// <summary>
        ///     Upload album (videos and photos)
        /// </summary>
        /// <param name="images">Array of photos to upload</param>
        /// <param name="videos">Array of videos to upload</param>
        /// <param name="caption">Caption</param>
        public async Task<IResult<InstaMedia>> UploadAlbumAsync(InstaImage[] images, InstaVideoUpload[] videos, string caption)
        {
            try
            {
                var imagesUploadIds = new string[images.Length];
                var index = 0;
                if (images != null)
                    foreach (var image in images)
                    {
                        var instaUri = UriCreator.GetUploadPhotoUri();
                        var uploadId = ApiRequestMessage.GenerateUploadId();
                        var requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent(uploadId), "\"upload_id\""},
                            {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {
                                new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                                "\"image_compression\""
                            },
                            {new StringContent("1"), "\"is_sidecar\""}
                        };
                        byte[] fileBytes;
                        if (image.ImageBytes == null)
                            fileBytes = File.ReadAllBytes(image.Uri);
                        else
                            fileBytes = image.ImageBytes;
                        var imageContent = new ByteArrayContent(fileBytes);
                        imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                        imageContent.Headers.Add("Content-Type", "application/octet-stream");
                        requestContent.Add(imageContent, "photo",
                            $"pending_media_{ApiRequestMessage.GenerateUploadId()}.jpg");
                        var request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        var response = await _httpRequestProcessor.SendAsync(request);
                        var json = await response.Content.ReadAsStringAsync();
                        if (response.IsSuccessStatusCode)
                            imagesUploadIds[index++] = uploadId;
                        else
                            return Result.UnExpectedResponse<InstaMedia>(response, json);
                    }

                var videosDic = new Dictionary<string, InstaVideo>();
                if (videos != null)
                    foreach (var video in videos)
                    {
                        var instaUri = UriCreator.GetUploadVideoUri();
                        var uploadId = ApiRequestMessage.GenerateUploadId();
                        var requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent("0"), "\"upload_media_height\""},
                            {new StringContent("1"), "\"is_sidecar\""},
                            {new StringContent("0"), "\"upload_media_width\""},
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                            {new StringContent("0"), "\"upload_media_duration_ms\""},
                            {new StringContent(uploadId), "\"upload_id\""},
                            {new StringContent("{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"), "\"retry_context\""},
                            {new StringContent("2"), "\"media_type\""},
                        };

                        var request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        var response = await _httpRequestProcessor.SendAsync(request);
                        var json = await response.Content.ReadAsStringAsync();
                        var videoResponse = JsonConvert.DeserializeObject<VideoUploadJobResponse>(json);
                        if (videoResponse == null)
                            return Result.Fail<InstaMedia>("Failed to get response from instagram video upload endpoint");

                        byte[] videoBytes;
                        if (video.Video.VideoBytes == null)
                            videoBytes = File.ReadAllBytes(video.Video.Uri);
                        else
                            videoBytes = video.Video.VideoBytes;
                        var first = videoResponse.VideoUploadUrls[0];
                        instaUri = new Uri(Uri.EscapeUriString(first.Url));


                        requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {
                                new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                                "\"image_compression\""
                            }
                        };
                        var videoContent = new ByteArrayContent(videoBytes);
                        videoContent.Headers.Add("Content-Transfer-Encoding", "binary");
                        videoContent.Headers.Add("Content-Type", "application/octet-stream");
                        videoContent.Headers.Add("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(video.Video.Uri)}\"");
                        requestContent.Add(videoContent);
                        request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        request.Headers.Host = "upload.instagram.com";
                        request.Headers.Add("Cookie2", "$Version=1");
                        request.Headers.Add("Session-ID", uploadId);
                        request.Headers.Add("job", first.Job);
                        response = await _httpRequestProcessor.SendAsync(request);
                        json = await response.Content.ReadAsStringAsync();


                        
                        instaUri = UriCreator.GetUploadPhotoUri();
                        requestContent = new MultipartFormDataContent(uploadId)
                        {
                            {new StringContent("1"), "\"is_sidecar\""},
                            {new StringContent(uploadId), "\"upload_id\""},
                            {new StringContent(_deviceInfo.DeviceGuid.ToString()), "\"_uuid\""},
                            {new StringContent(_user.CsrfToken), "\"_csrftoken\""},
                            {
                                new StringContent("{\"lib_name\":\"jt\",\"lib_version\":\"1.3.0\",\"quality\":\"87\"}"),
                                "\"image_compression\""
                            },
                            {new StringContent("{\"num_step_auto_retry\":0,\"num_reupload\":0,\"num_step_manual_retry\":0}"), "\"retry_context\""},
                            {new StringContent("2"), "\"media_type\""},
                        };
                        byte[] imageBytes;
                        if (video.VideoThumbnail.ImageBytes == null)
                            imageBytes = File.ReadAllBytes(video.VideoThumbnail.Uri);
                        else
                            imageBytes = video.VideoThumbnail.ImageBytes;
                        var imageContent = new ByteArrayContent(imageBytes);
                        imageContent.Headers.Add("Content-Transfer-Encoding", "binary");
                        imageContent.Headers.Add("Content-Type", "application/octet-stream");
                        requestContent.Add(imageContent, "photo", $"cover_photo_{uploadId}.jpg");
                        request = HttpHelper.GetDefaultRequest(HttpMethod.Post, instaUri, _deviceInfo);
                        request.Content = requestContent;
                        response = await _httpRequestProcessor.SendAsync(request);
                        json = await response.Content.ReadAsStringAsync();
                        var imgResp = JsonConvert.DeserializeObject<ImageThumbnailResponse>(json);
                        videosDic.Add(uploadId, video.Video);
                    }
                var config = await ConfigureAlbumAsync(imagesUploadIds, videosDic, caption);
                return config;
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }
        /// <summary>
        ///     Configure photo
        /// </summary>
        /// <param name="image">Photo to configure</param>
        /// <param name="uploadId">Upload id</param>
        /// <param name="caption">Caption</param>
        /// <returns></returns>
        private async Task<IResult<InstaMedia>> ConfigurePhotoAsync(InstaImage image, string uploadId, string caption)
        {
            try
            {
                var instaUri = UriCreator.GetMediaConfigureUri();
                var androidVersion = _deviceInfo.AndroidVersion;
                    //AndroidVersion.FromString(_deviceInfo.FirmwareFingerprint.Split('/')[2].Split(':')[1]);
                if (androidVersion == null)
                    return Result.Fail("Unsupported android version", (InstaMedia) null);
                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"media_folder", "Camera"},
                    {"source_type", "4"},
                    {"caption", caption},
                    {"upload_id", uploadId},
                    {
                        "device", new JObject
                        {
                            {"manufacturer", _deviceInfo.HardwareManufacturer},
                            {"model", _deviceInfo.HardwareModel},
                            {"android_version", androidVersion.VersionNumber},
                            {"android_release", androidVersion.APILevel}
                        }
                    },
                    {
                        "edits", new JObject
                        {
                            {"crop_original_size", new JArray {image.Width, image.Height}},
                            {"crop_center", new JArray {0.0, -0.0}},
                            {"crop_zoom", 1}
                        }
                    },
                    {
                        "extra", new JObject
                        {
                            {"source_width", image.Width},
                            {"source_height", image.Height}
                        }
                    }
                };
                var request = HttpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    return Result.UnExpectedResponse<InstaMedia>(response, json);
                var mediaResponse =
                    JsonConvert.DeserializeObject<InstaMediaItemResponse>(json, new InstaMediaDataConverter());
                var converter = ConvertersFabric.Instance.GetSingleMediaConverter(mediaResponse);
                return Result.Success(converter.Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }

        private async Task<IResult<InstaMedia>> ConfigureAlbumAsync(string[] imagesUploadId, Dictionary<string,InstaVideo> videos, string caption)
        {
            try
            {
                var instaUri = UriCreator.GetMediaAlbumConfigureUri();
                var clientSidecarId = ApiRequestMessage.GenerateUploadId();
                var childrenArray = new JArray();
                foreach (var id in imagesUploadId)
                    childrenArray.Add(new JObject
                    {
                        {"timezone_offset", "16200"},
                        {"source_type", 4},
                        {"upload_id", id},
                        {"caption", ""},
                    });

                foreach (var id in videos)
                {
                    if (id.Value.Width == 0)
                        id.Value.Width = 640;
                    if (id.Value.Height == 0)
                        id.Value.Height = 640;
                    childrenArray.Add(new JObject
                    {
                        {"timezone_offset", "16200"},
                        {"caption", ""},
                        {"upload_id", id.Key},
                        {"date_time_original", DateTime.Now.ToString("yyyy-dd-MMTh:mm:ss-0fffZ")},
                        {"source_type", "4"},
                        {
                            "extra", JsonConvert.SerializeObject(new JObject
                            {
                                {"source_width", 0},
                                {"source_height", 0}
                            })
                        },
                        {
                            "clips", JsonConvert.SerializeObject(new JArray{
                                new JObject
                                {
                                    {"length", id.Value.Length},
                                    {"source_type", "4"},
                                }
                            })
                        },
                        {
                            "device", JsonConvert.SerializeObject(new JObject{
                                {"manufacturer", _deviceInfo.HardwareManufacturer},
                                {"model", _deviceInfo.DeviceModelIdentifier},
                                {"android_release", _deviceInfo.AndroidVersion.VersionNumber},
                                {"android_version", _deviceInfo.AndroidVersion.APILevel}
                            })
                        },
                        {"length", id.Value.Length},
                        {"poster_frame_index", 0},
                        {"audio_muted", false},
                        {"filter_type", "0"},
                        {"video_result", "deprecated"},
                    });
                }
                var data = new JObject
                {
                    {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                    {"_uid", _user.LoggedInUser.Pk},
                    {"_csrftoken", _user.CsrfToken},
                    {"caption", caption},
                    {"client_sidecar_id", clientSidecarId},
                    {"upload_id", clientSidecarId},
                    {
                        "device", new JObject
                        {
                            {"manufacturer", _deviceInfo.HardwareManufacturer},
                            {"model", _deviceInfo.DeviceModelIdentifier},
                            {"android_release", _deviceInfo.AndroidVersion.VersionNumber},
                            {"android_version", _deviceInfo.AndroidVersion.APILevel}
                        }
                    },
                    {"children_metadata", childrenArray},
                };

                var request = HttpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, data);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return Result.UnExpectedResponse<InstaMedia>(response, json);
                var mediaResponse = JsonConvert.DeserializeObject<InstaMediaAlbumResponse>(json);
                var converter = ConvertersFabric.Instance.GetSingleMediaFromAlbumConverter(mediaResponse);
                var obj = converter.Convert();
                return Result.Success(obj);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }
        /// <summary>
        ///     Get users (short) who liked certain media. Normaly it return around 1000 last users.
        /// </summary>
        /// <param name="mediaId">Media id</param>
        public async Task<IResult<InstaLikersList>> GetMediaLikersAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var likers = new InstaLikersList();
                var likersUri = UriCreator.GetMediaLikersUri(mediaId);
                var request = HttpHelper.GetDefaultRequest(HttpMethod.Get, likersUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaLikersList>(response, json);
                var mediaLikersResponse = JsonConvert.DeserializeObject<InstaMediaLikersResponse>(json);
                likers.UsersCount = mediaLikersResponse.UsersCount;
                if (mediaLikersResponse.UsersCount < 1) return Result.Success(likers);
                likers.AddRange(
                    mediaLikersResponse.Users.Select(ConvertersFabric.Instance.GetUserShortConverter)
                        .Select(converter => converter.Convert()));
                return Result.Success(likers);
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaLikersList>(exception);
            }
        }
        /// <summary>
        ///     Like media (photo or video)
        /// </summary>
        /// <param name="mediaId">Media id</param>
        public async Task<IResult<bool>> LikeMediaAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                return await LikeUnlikeMediaInternal(mediaId, UriCreator.GetLikeMediaUri(mediaId));
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }
        /// <summary>
        ///     Remove like from media (photo or video)
        /// </summary>
        /// <param name="mediaId">Media id</param>
        public async Task<IResult<bool>> UnLikeMediaAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                return await LikeUnlikeMediaInternal(mediaId, UriCreator.GetUnLikeMediaUri(mediaId));
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<bool>(exception);
            }
        }
        /// <summary>
        ///     Get media by its id asynchronously
        /// </summary>
        /// <param name="mediaId">Maximum count of pages to retrieve</param>
        /// <returns>
        ///     <see cref="InstaMedia" />
        /// </returns>
        public async Task<IResult<InstaMedia>> GetMediaByIdAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var mediaUri = UriCreator.GetMediaUri(mediaId);
                var request = HttpHelper.GetDefaultRequest(HttpMethod.Get, mediaUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<InstaMedia>(response, json);
                var mediaResponse = JsonConvert.DeserializeObject<InstaMediaListResponse>(json,
                    new InstaMediaListDataConverter());
                if (mediaResponse.Medias?.Count > 1)
                {
                    var errorMessage = $"Got wrong media count for request with media id={mediaId}";
                    _logger?.LogInfo(errorMessage);
                    return Result.Fail<InstaMedia>(errorMessage);
                }

                var converter =
                    ConvertersFabric.Instance.GetSingleMediaConverter(mediaResponse.Medias.FirstOrDefault());
                return Result.Success(converter.Convert());
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<InstaMedia>(exception);
            }
        }
        /// <summary>
        ///     Get share link from media Id
        /// </summary>
        /// <param name="mediaId">media ID</param>
        /// <returns>Share link as Uri</returns>
        public async Task<IResult<Uri>> GetShareLinkFromMediaIdAsync(string mediaId)
        {
            UserAuthValidator.Validate(_userAuthValidate);
            try
            {
                var collectionUri = UriCreator.GetShareLinkFromMediaId(mediaId);
                var request = HttpHelper.GetDefaultRequest(HttpMethod.Get, collectionUri, _deviceInfo);
                var response = await _httpRequestProcessor.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();

                if (response.StatusCode != HttpStatusCode.OK)
                    return Result.UnExpectedResponse<Uri>(response, json);

                var data = JsonConvert.DeserializeObject<InstaPermalinkResponse>(json);
                return Result.Success(new Uri(data.Permalink));
            }
            catch (Exception exception)
            {
                _logger?.LogException(exception);
                return Result.Fail<Uri>(exception.Message);
            }
        }

        private async Task<IResult<bool>> LikeUnlikeMediaInternal(string mediaId, Uri instaUri)
        {
            var fields = new Dictionary<string, string>
            {
                {"_uuid", _deviceInfo.DeviceGuid.ToString()},
                {"_uid", _user.LoggedInUser.Pk.ToString()},
                {"_csrftoken", _user.CsrfToken},
                {"media_id", mediaId}
            };
            var request =
                HttpHelper.GetSignedRequest(HttpMethod.Post, instaUri, _deviceInfo, fields);
            var response = await _httpRequestProcessor.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            return response.StatusCode == HttpStatusCode.OK
                ? Result.Success(true)
                : Result.UnExpectedResponse<bool>(response, json);
        }
    }
}