// =============================================================================
//  ForZip — Forensic ZIP Tool
//  Open-source forensic compression and verification utility
// =============================================================================
//
//  Author : Claudio Andino
//  Email  : claudio@patagoniarobot.com
//
//  Copyright (c) 2026 Claudio Andino
//  Developed under the Patagonia Robot initiative
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at:
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//
// =============================================================================

using System.Security.Cryptography;
using ForZip.Core.Interfaces;
using ForZip.Core.Models;

namespace ForZip.Core.Services;

public class PasswordService : IPasswordService
{
    private const int MinLength = 8;
    private const int MaxLength = 128;

    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";

    // Conjunto completo de 32 símbolos ASCII imprimibles (sin alfanuméricos ni espacio)
    private const string Symbols = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    // Caracteres considerados ambiguos para lectura humana (cero/o, uno/L/i, pipe)
    private const string AmbiguousChars = "0OoIl1|";

    public string GeneratePassword(PasswordOptions options)
    {
        if (options.Length < MinLength || options.Length > MaxLength)
        {
            throw new ArgumentException(
                $"La longitud debe estar entre {MinLength} y {MaxLength}.",
                nameof(options));
        }

        var classes = BuildEnabledClasses(options);
        if (classes.Count == 0)
        {
            throw new ArgumentException(
                "Debe activarse al menos una clase de caracteres.",
                nameof(options));
        }

        // Pool global = unión de las clases activas
        var pool = string.Concat(classes);

        var passwordChars = new char[options.Length];

        // Garantizar al menos un carácter de cada clase activa
        for (int i = 0; i < classes.Count; i++)
        {
            passwordChars[i] = PickRandomChar(classes[i]);
        }

        // Llenar el resto con caracteres del pool global
        for (int i = classes.Count; i < options.Length; i++)
        {
            passwordChars[i] = PickRandomChar(pool);
        }

        // Mezclar para que las posiciones garantizadas no queden siempre al principio
        FisherYatesShuffle(passwordChars);

        return new string(passwordChars);
    }

    public double CalculateEntropy(PasswordOptions options, int length)
    {
        var classes = BuildEnabledClasses(options);
        var poolSize = classes.Sum(c => c.Length);
        if (poolSize == 0 || length <= 0)
        {
            return 0.0;
        }

        return length * Math.Log2(poolSize);
    }

    private static List<string> BuildEnabledClasses(PasswordOptions options)
    {
        var classes = new List<string>();

        if (options.IncludeUppercase)
        {
            classes.Add(FilterAmbiguous(Uppercase, options.ExcludeAmbiguous));
        }

        if (options.IncludeLowercase)
        {
            classes.Add(FilterAmbiguous(Lowercase, options.ExcludeAmbiguous));
        }

        if (options.IncludeDigits)
        {
            classes.Add(FilterAmbiguous(Digits, options.ExcludeAmbiguous));
        }

        if (options.IncludeSymbols)
        {
            classes.Add(FilterAmbiguous(Symbols, options.ExcludeAmbiguous));
        }

        // Descartar clases que quedaron vacías tras filtrar
        classes.RemoveAll(string.IsNullOrEmpty);
        return classes;
    }

    private static string FilterAmbiguous(string source, bool excludeAmbiguous)
    {
        if (!excludeAmbiguous)
        {
            return source;
        }

        var filtered = new System.Text.StringBuilder(source.Length);
        foreach (var c in source)
        {
            if (AmbiguousChars.IndexOf(c) < 0)
            {
                filtered.Append(c);
            }
        }

        return filtered.ToString();
    }

    private static char PickRandomChar(string pool)
    {
        var index = NextUniformIndex(pool.Length);
        return pool[index];
    }

    // Rejection sampling: evita el sesgo modular al mapear bytes a un rango arbitrario
    private static int NextUniformIndex(int range)
    {
        if (range <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(range));
        }

        // Mayor múltiplo de 'range' que cabe en un byte (256)
        int limit = 256 - (256 % range);
        Span<byte> buffer = stackalloc byte[1];

        while (true)
        {
            RandomNumberGenerator.Fill(buffer);
            byte b = buffer[0];
            if (b < limit)
            {
                return b % range;
            }
            // Si cae fuera del límite, descartar y volver a generar
        }
    }

    private static void FisherYatesShuffle(char[] array)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = NextUniformIndex(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }
}
