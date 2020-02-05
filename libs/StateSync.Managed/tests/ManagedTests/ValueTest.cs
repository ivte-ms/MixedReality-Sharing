﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Xunit;

namespace Microsoft.MixedReality.Sharing.StateSync.Test
{
    public class ValueTest
    {
        static byte[] bytesFoo = new byte[] { 102, 111, 111 };  // "foo"
        static byte[] bytesWithZeros = new byte[] { 1, 100, 2, 200, 0, 255, 0, 255 };

        [Fact]
        public void Create()
        {
            Blob valueFoo = new Blob(bytesFoo);
            Assert.True(valueFoo.ToSpan().SequenceEqual(bytesFoo));

            Blob valueWithZeros = new Blob(bytesWithZeros);
            Assert.True(valueWithZeros.ToSpan().SequenceEqual(bytesWithZeros));
        }
    }
}
