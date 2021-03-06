﻿/*
 * Copyright (c) 2012-2017 Snowflake Computing Inc. All rights reserved.
 */

using System;
using System.IO.Compression;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using System.Diagnostics;
using Newtonsoft.Json.Serialization;

namespace Snowflake.Data.Core
{
    class SFChunkDownloader
    {
        private List<SFResultChunk> chunks;

        private string qrmk;

        private int colCount;

        private int nextChunkToDownloadIndex;

        private int nextChunkToConsumeIndex;
        
        // TODO: parameterize prefetch slot
        const int prefetchSlot = 2;

        private static IRestRequest restRequest = RestRequestImpl.Instance;

        private static JsonSerializer jsonSerializer = new JsonSerializer();

        private Dictionary<string, string> chunkHeaders;

        public SFChunkDownloader(int colCount, List<ExecResponseChunk>chunkInfos, string qrmk, 
            Dictionary<string, string> chunkHeaders)
        {
            
            this.colCount = colCount;
            this.qrmk = qrmk;
            this.chunkHeaders = chunkHeaders;
            this.chunks = new List<SFResultChunk>();
            this.nextChunkToDownloadIndex = 0;
            this.nextChunkToConsumeIndex = 0;

            foreach(ExecResponseChunk chunkInfo in chunkInfos)
            {
                this.chunks.Add(new SFResultChunk(chunkInfo.url, chunkInfo.rowCount, colCount));
            }

            startNextDownload();
        }

        private void startNextDownload()
        {
            while(nextChunkToDownloadIndex - nextChunkToConsumeIndex < prefetchSlot && nextChunkToDownloadIndex < chunks.Count)
            {
                DownloadContext downloadContext = new DownloadContext()
                {
                    chunk = chunks[nextChunkToDownloadIndex],
                    chunkIndex = nextChunkToDownloadIndex,
                    qrmk = this.qrmk,
                    chunkHeaders = this.chunkHeaders
                };
                ThreadPool.QueueUserWorkItem(new WaitCallback(downloadChunkCallBack), downloadContext);
                nextChunkToDownloadIndex++;
            }
        }

        public SFResultChunk getNextChunkToConsume()
        {
            // prefetch 
            startNextDownload();

            SFResultChunk currentChunk = this.chunks[nextChunkToConsumeIndex];

            if (currentChunk.downloadState == DownloadState.SUCCESS)
            {
                nextChunkToConsumeIndex++;
                return currentChunk; 
            }
            else
            {
                // wait until donwload finish
                lock(currentChunk.syncPrimitive)
                {
                    while(currentChunk.downloadState == DownloadState.IN_PROGRESS 
                        || currentChunk.downloadState == DownloadState.NOT_STARTED)
                    {
                        Monitor.Wait(currentChunk.syncPrimitive);
                    }
                }
                nextChunkToConsumeIndex++;
                return currentChunk;
            }
        } 
        static void downloadChunkCallBack(Object context)
        {
            DownloadContext downloadContext = (DownloadContext)context;
            SFResultChunk chunk = downloadContext.chunk;

            // change download status
            Monitor.Enter(chunk.syncPrimitive);
            try
            {
                chunk.downloadState = DownloadState.IN_PROGRESS;
            }
            finally
            {
                Monitor.Exit(chunk.syncPrimitive);
            }

            S3DownloadRequest downloadRequest = new S3DownloadRequest()
            {
                uri = new UriBuilder(chunk.url).Uri,
                qrmk = downloadContext.qrmk,
                // s3 download request timeout to one hour
                timeout = TimeSpan.FromHours(1),
                httpRequestTimeout = TimeSpan.FromSeconds(16),
                chunkHeaders = downloadContext.chunkHeaders
            };

            HttpResponseMessage httpResponse = restRequest.get(downloadRequest);
            Stream stream = httpResponse.Content.ReadAsStreamAsync().Result;
            IEnumerable<string> encoding;
            if (httpResponse.Content.Headers.TryGetValues("Content-Encoding", out encoding))
            {
                if (String.Compare(encoding.First(), "gzip", true) == 0)
                {
                    stream = new GZipStream(stream, CompressionMode.Decompress);
                }
            }

            parseStreamIntoChunk(stream, chunk);

            /*StreamReader r = new StreamReader(stream);
            string l = r.ReadLine();
            Console.WriteLine(l);*/

            chunk.downloadState = DownloadState.SUCCESS;

            // signal main thread to start consuming 
            lock(chunk.syncPrimitive)
            {
                Monitor.Pulse(chunk.syncPrimitive);
            }
        }
        
        /// <summary>
        ///     Content from s3 in format of 
        ///     ["val1", "val2", null, ...],
        ///     ["val3", "val4", null, ...],
        ///     ...
        ///     To parse it as a json, we need to preappend '[' and append ']' to the stream 
        /// </summary>
        /// <param name="content"></param>
        /// <param name="resultChunk"></param>
        private static void parseStreamIntoChunk(Stream content, SFResultChunk resultChunk)
        {
            Stream openBracket = new MemoryStream(Encoding.UTF8.GetBytes("["));
            Stream closeBracket = new MemoryStream(Encoding.UTF8.GetBytes("]"));

            Stream concatStream = new ConcatenatedStream(new Stream[3] { openBracket, content, closeBracket});

            // parse results row by row
            using (StreamReader sr = new StreamReader(concatStream))
            using (JsonTextReader jr = new JsonTextReader(sr))
            {
                resultChunk.rowSet = jsonSerializer.Deserialize<string[,]>(jr);
            }
        }
    }

    class DownloadContext
    {
        public SFResultChunk chunk { get; set; }

        public int chunkIndex { get; set; }

        public string qrmk { get; set; }

        public Dictionary<string, string> chunkHeaders { get; set; }
    }
    
    /// <summary>
    ///     Used to concat multiple streams without copying. Since we need to preappend '[' and append ']'
    /// </summary>
    class ConcatenatedStream : Stream
    {
        Queue<Stream> streams;

        public ConcatenatedStream(IEnumerable<Stream> streams)
        {
            this.streams = new Queue<Stream>(streams);
        }

        public override bool CanRead
        {
            get { return true; }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (streams.Count == 0)
                return 0;

            int bytesRead = streams.Peek().Read(buffer, offset, count);
            if (bytesRead == 0)
            {
                streams.Dequeue().Dispose();
                bytesRead += Read(buffer, offset + bytesRead, count - bytesRead);
            }
            return bytesRead;
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Length
        {
            get { throw new NotImplementedException(); }
        }

        public override long Position
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }

}
