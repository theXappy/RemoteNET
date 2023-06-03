#region License
/*
 * Copyright (C) 2022-2023 Stefano Moioli <smxdev4@gmail.com>
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */
#endregion

using System.Text;

namespace ScubaDiver.Demangle.Demangle.Core
{
    public static class ByteArrayExtensions
    {
        public static string AsString(this byte[] arr, Encoding encoding)
        {
            return encoding.GetString(arr).TrimEnd((char) 0);
        }
    }
}
