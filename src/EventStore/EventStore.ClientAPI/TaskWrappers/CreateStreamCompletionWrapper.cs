﻿// Copyright (c) 2012, Event Store LLP
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
// 
// Redistributions of source code must retain the above copyright notice,
// this list of conditions and the following disclaimer.
// Redistributions in binary form must reproduce the above copyright
// notice, this list of conditions and the following disclaimer in the
// documentation and/or other materials provided with the distribution.
// Neither the name of the Event Store LLP nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// 

using System;
using System.Threading.Tasks;
using EventStore.ClientAPI.Commands;
using EventStore.ClientAPI.Defines;
using EventStore.ClientAPI.Exceptions;
using EventStore.ClientAPI.Tcp;
using EventStore.ClientAPI.Transport.Tcp;

namespace EventStore.ClientAPI.TaskWrappers
{
    internal class CreateStreamCompletionWrapper : ITaskCompletionWrapper
    {
        private readonly TaskCompletionSource<CreateStreamResult> _completion;
        private ClientMessages.CreateStreamCompleted _result;

        private Guid _correlationId;
        private readonly object _corrIdLock = new object();

        private readonly string _stream;
        private readonly byte[] _metadata;

        public Guid CorrelationId
        {
            get
            {
                lock (_corrIdLock)
                    return _correlationId;
            }
        }

        public CreateStreamCompletionWrapper(TaskCompletionSource<CreateStreamResult> completion,
                                             Guid correlationId,
                                             string stream,
                                             byte[] metadata)
        {
            _completion = completion;

            _correlationId = correlationId;
            _stream = stream;
            _metadata = metadata;
        }

        public void SetRetryId(Guid correlationId)
        {
            lock (_corrIdLock)
                _correlationId = correlationId;
        }

        public TcpPackage CreateNetworkPackage()
        {
            lock (_corrIdLock)
            {
                var dto = new ClientMessages.CreateStream(_correlationId, _stream, _metadata);
                return new TcpPackage(TcpCommand.CreateStream, _correlationId, dto.Serialize());
            }
        }

        public ProcessResult Process(TcpPackage package)
        {
            try
            {
                if (package.Command != TcpCommand.CreateStreamCompleted)
                    return new ProcessResult(ProcessResultStatus.NotifyError, 
                                             new CommandNotExpectedException(TcpCommand.CreateStreamCompleted.ToString(), 
                                                                             package.Command.ToString()));

                var data = package.Data;
                var dto = data.Deserialize<ClientMessages.CreateStreamCompleted>();
                _result = dto;

                switch ((OperationErrorCode)dto.ErrorCode)
                {
                    case OperationErrorCode.Success:
                        return new ProcessResult(ProcessResultStatus.Success);

                    case OperationErrorCode.PrepareTimeout:
                    case OperationErrorCode.CommitTimeout:
                    case OperationErrorCode.ForwardTimeout:
                        return new ProcessResult(ProcessResultStatus.Retry);
                    case OperationErrorCode.WrongExpectedVersion:
                    case OperationErrorCode.StreamDeleted:
                    case OperationErrorCode.InvalidTransaction:
                        return new ProcessResult(ProcessResultStatus.NotifyError,
                                                 new Exception(string.Format("{0}", (OperationErrorCode)dto.ErrorCode)));
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception e)
            {
                return new ProcessResult(ProcessResultStatus.NotifyError, e);
            }
        }

        public void Complete()
        {
            if (_result != null)
                _completion.SetResult(new CreateStreamResult((OperationErrorCode)_result.ErrorCode));
            else
                _completion.SetException(new NoResultException());
        }

        public void Fail(Exception exception)
        {
            _completion.SetException(exception);
        }
    }
}