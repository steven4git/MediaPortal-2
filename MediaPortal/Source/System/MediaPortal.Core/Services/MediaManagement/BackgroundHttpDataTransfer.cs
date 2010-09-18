#region Copyright (C) 2007-2010 Team MediaPortal

/*
    Copyright (C) 2007-2010 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2.  If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using MediaPortal.Core.Logging;
using MediaPortal.Utilities.SystemAPI;

namespace MediaPortal.Core.Services.MediaManagement
{
  public class BackgroundHttpDataTransfer : IDisposable
  {
    #region Consts

    protected const long BLOCK_DIVIDE_THRESHOLD = 4096;
    protected const long READ_BUFFER_SIZE = 16384;

    protected const string PRODUCT_VERSION = "MediaPortal/2.0";

    /// <summary>
    /// Timeout for a pending HTTP request in seconds.
    /// </summary>
    protected const int PENDING_REQUEST_TIMEOUT = 10;

    #endregion

    #region Classes

    protected class PendingBlock
    {
      protected long _start;
      protected long _end;

      public PendingBlock(long start, long end)
      {
        _start = start;
        _end = end;
      }

      public long Start
      {
        get { return _start; }
        set { _start = value; }
      }

      public long End
      {
        get { return _end; }
        set { _end = value; }
      }
    }

    protected class EndOfBlockComparer : IComparer<PendingBlock>
    {
      public int Compare(PendingBlock x, PendingBlock y)
      {
        return x.End.CompareTo(y.End);
      }
    }

    #endregion

    protected object _syncObj = new object();
    protected List<PendingBlock> _pendingBlocks = new List<PendingBlock>(10);
    protected HttpWebRequest _pendingRequest = null;
    protected string _resourceURL;
    protected Stream _bufferStream;
    protected long _position = 0;
    protected bool _terminated = false;
    protected string _errorMessage = null;
    protected bool _continue = false; // Flag to suppress error handling - the transfer should be continued after a break of the current stream - for seeking to another position

    protected static string _userAgent;

    static BackgroundHttpDataTransfer()
    {
      _userAgent = WindowsAPI.GetOsVersionString() + " HTTP/1.1 " + PRODUCT_VERSION;
    }

    public BackgroundHttpDataTransfer(string resourceURL, Stream writeStream)
    {
      _resourceURL = resourceURL;
      _bufferStream = writeStream;
      _pendingBlocks.Add(new PendingBlock(0, writeStream.Length - 1));
      RequestNextBlock();
    }

    public void Dispose()
    {
      Terminate();
    }

    public object SyncObj
    {
      get { return _syncObj; }
    }

    public string ResourceURL
    {
      get { return _resourceURL; }
    }

    public string ErrorMessage
    {
      get { return _errorMessage; }
    }

    public int ReadData(byte[] buffer, int offset, int count)
    {
      lock (_syncObj)
      {
        int numRead;
        while (true)
        {
          numRead = count;
          // First check where the next pending block begins
          int index = GetNextBlockIndex(_position, false);
          PendingBlock block = null;
          if (index != -1)
          { // There is a remaining block behind our position - check if we have enough data left
            block = _pendingBlocks[index];
            numRead = Math.Min(numRead, (int) (block.Start - _position));
          }
          numRead = Math.Min(numRead, (int) (_bufferStream.Length - _position));
          if (numRead < 0)
            return 0;
          if (numRead == 0 && block != null && _pendingRequest != null)
            Monitor.Wait(_syncObj);
          else
            // count > 0 => data available
            // block == null => no pending data at current read position
            // _pendingRequest == null => transfer completed or error during transfer
            break;
        }
        if (_errorMessage != null)
          throw new IOException(_errorMessage);
        _bufferStream.Position = _position;
        _position += numRead;
        return _bufferStream.Read(buffer, offset, numRead);
      }
    }

    public void Seek(long position)
    {
      lock (_syncObj)
      {
        _position = position;
        _pendingRequest.Abort();
        _continue = true;
      }
    }

    public void Terminate()
    {
      HttpWebRequest request;
      lock (_syncObj)
      {
        _terminated = true;
        request = _pendingRequest;
        _pendingRequest = null;
        Monitor.PulseAll(_syncObj);
      }
      if (request != null)
        request.Abort();
    }

    protected void RequestNextBlock()
    {
      lock (_syncObj)
        try
        {
          if (_terminated || _errorMessage != null)
            return;
          int index = GetNextBlockIndex(_position, true);
          if (index == -1)
            return;
          PendingBlock block = _pendingBlocks[index];
          if (block.Start < _position - BLOCK_DIVIDE_THRESHOLD)
          {
            _pendingBlocks.Insert(index, new PendingBlock(block.Start, _position - 1));
            block.Start = _position;
          }
          HttpWebRequest request = (HttpWebRequest) WebRequest.Create(_resourceURL);
          request.Method = "GET";
          request.KeepAlive = true;
          request.AllowAutoRedirect = true;
          request.UserAgent = _userAgent;
          request.AddRange((int) block.Start, (int) block.End);

          IAsyncResult result = request.BeginGetResponse(OnResponseReceived, null);
          // Set _pendingRequest after we can be sure that OnResponseReceived will be called.
          _pendingRequest = request;
          AddTimeout(_pendingRequest, result, PENDING_REQUEST_TIMEOUT*1000);
        }
        catch (Exception e)
        {
          ServiceRegistration.Get<ILogger>().Warn("BackgroundHttpDataTransfer: Error requesting data from remote server", e);
          SetError(e.Message);
        }
    }

    private void OnResponseReceived(IAsyncResult asyncResult)
    {
      lock (_syncObj)
      {
        try
        {
          if (_terminated)
            return;
          long bufferStreamLength = _bufferStream.Length;
          HttpWebResponse response = (HttpWebResponse) _pendingRequest.EndGetResponse(asyncResult);
          try
          {
            long from;
            long to;
            long length;
            String contentRange = response.GetResponseHeader("Content-Range");
            if (string.IsNullOrEmpty(contentRange))
            { // No content range given, use complete range
              from = 0;
              to = response.ContentLength - 1;
              length = response.ContentLength;
            }
            else if (!ParseContentRange(contentRange, out from, out to, out length) ||
                (length != -1 && length != bufferStreamLength))
            { // Fatal: Server commmunication failed
              SetError("Protocol error");
              return;
            }
            using (Stream body = response.GetResponseStream())
              ProcessResult(body, from, to, length);
            Monitor.PulseAll(_syncObj);
          }
          finally
          {
            response.Close();
          }
        }
        catch (WebException e)
        {
          if (!_terminated && !_continue)
          {
            ServiceRegistration.Get<ILogger>().Warn("BackgroundHttpDataTransfer: Problem receiving file part", e);
            SetError(e.Message);
          }
        }
        finally
        {
          _pendingRequest = null;
          _continue = false;
        }
        RequestNextBlock();
      }
    }

    protected void SetError(string message)
    {
      _errorMessage = message;
      Monitor.PulseAll(_syncObj);
    }

    protected bool ParseContentRange(string contentRange, out long from, out long to, out long length)
    {
      from = -1;
      to = -1;
      length = -1;
      if (!contentRange.StartsWith("bytes "))
        return false;
      string byteRange = contentRange.Substring("bytes ".Length);
      int i = byteRange.IndexOf('-');
      int j = byteRange.IndexOf('/');
      if (i == -1 || j == -1)
        return false;
      from = long.Parse(byteRange.Substring(0, i).Trim());
      to = long.Parse(byteRange.Substring(i + 1, j - i - 1).Trim());
      string lengthStr = byteRange.Substring(j + 1).Trim();
      if (lengthStr == "*")
        length = -1;
      else
        length = long.Parse(lengthStr);
      return true;
    }

    protected void ProcessResult(Stream stream, long from, long to, long length)
    {
      int currentBlockIndex;
      PendingBlock currentBlock;
      lock (_syncObj)
      {
        if (to > _bufferStream.Length)
        { // Fatal: More bytes in stream than expected
          SetError("Protocol error");
          return;
        }
        currentBlockIndex = GetNextBlockIndex(from, false);
        if (currentBlockIndex == -1)
        { // We got a content range which doesn't overlap with any pending block
          SetError("Protocol error");
          return;
        }
        currentBlock = _pendingBlocks[currentBlockIndex];
      }
      byte[] buffer = new byte[READ_BUFFER_SIZE];
      long nextWritePos = from;
      long remaining = length;
      int numBytesInBuffer;
      while ((numBytesInBuffer = stream.Read(buffer, 0, (int) Math.Min(READ_BUFFER_SIZE, remaining))) != 0)
      {
        lock (_syncObj)
        { // Lock inside the loop to give readers a chance to read while we're writing
          _bufferStream.Position = nextWritePos;
          remaining -= numBytesInBuffer;
          _bufferStream.Write(buffer, 0, numBytesInBuffer);
          nextWritePos += numBytesInBuffer;
          if (currentBlock.Start < nextWritePos)
          { // Inside pending block, modify block
            if (currentBlock.End < nextWritePos)
            { // Block complete
              _pendingBlocks.RemoveAt(currentBlockIndex);
              currentBlockIndex = GetNextBlockIndex(nextWritePos, false);
              if (currentBlockIndex == -1)
                // No next block available after the completed one
                return;
              currentBlock = _pendingBlocks[currentBlockIndex];
            }
            else
              currentBlock.Start = nextWritePos;
          }
        }
      }
    }

    protected int GetNextBlockIndex(long position, bool wrap)
    {
      lock (_syncObj)
      {
        if (_pendingBlocks.Count == 0)
          return -1;
        int index = _pendingBlocks.BinarySearch(new PendingBlock(0, _position), new EndOfBlockComparer());
        if (index < 0)
          index = ~index;
        return index == _pendingBlocks.Count ? (wrap ? 0 : -1) : index;
      }
    }

    private static void OnPendingRequestTimeout(object state, bool timedOut) {
      if (timedOut) {
        HttpWebRequest request = (HttpWebRequest) state;
        if (request != null)
          request.Abort();
      }
    }

    /// <summary>
    /// Aborts the given web <paramref name="request"/> if the given asynch <paramref name="result"/> doesn't return
    /// in <paramref name="timeoutMsecs"/> milli seconds.
    /// </summary>
    /// <param name="request">Request to track. Will be aborted (see <see cref="HttpWebRequest.Abort"/>) if the given
    /// asynchronous <paramref name="result"/> handle doen't return in the given time.</param>
    /// <param name="result">Asynchronous result handle to track. Should have been returned by a BeginXXX method of
    /// the given <paramref name="request"/>.</param>
    /// <param name="timeoutMsecs">Timeout in milliseconds, after that the request will be aborted.</param>
    public static void AddTimeout(HttpWebRequest request, IAsyncResult result, uint timeoutMsecs)
    {
      ThreadPool.RegisterWaitForSingleObject(result.AsyncWaitHandle, OnPendingRequestTimeout,
          request, timeoutMsecs, true);
    }
  }
}
