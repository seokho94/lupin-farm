using System.Collections.Generic;

namespace LupinFarm.Data
{
    /// <summary>
    /// 8×8 도트. 글자 하나가 팔레트 색 하나다. 작물 그림에서 '.'은 투명(아래 흙이 보인다).
    /// 오른쪽 끝 열과 아래쪽 끝 줄은 풀(g)이라 칸 사이가 갈라져 보인다.
    /// </summary>
    public static class Sprites
    {
        public const int Size = 8;

        public static readonly Dictionary<char, int> Palette = new Dictionary<char, int>
        {
            { 'g', 0x7CB65A }, // 풀 (봄)
            { 'G', 0x5E9A44 }, // 진한 풀
            { 's', 0x8B6B47 }, // 마른 흙
            { 'S', 0x74583A }, // 마른 흙 고랑
            { 'm', 0x5C4430 }, // 젖은 흙
            { 'M', 0x4A3626 }, // 젖은 흙 고랑
            { 'e', 0xD9C27A }, // 씨앗
            { 'l', 0x8FD16A }, // 연한 잎
            { 'L', 0x3F8F35 }, // 진한 잎
            { 't', 0xF0EAD8 }, // 순무 흰 부분
            { 'p', 0x9B5FC0 }, // 순무 보라
            { 'y', 0xF2C94C }, // 옥수수
            { 'Y', 0xC9A43A }, // 옥수수 수염
            { 'o', 0xE8833A }, // 당근
            { 'O', 0xC0602A }, // 진한 당근
            { 'r', 0xD9534F }, // 토마토, 기본 열매
            { 'b', 0xD6B272 }, // 감자 (흙보다 밝게)
            { 'k', 0xE5484D }, // 딸기
            { 'K', 0xF6D365 }, // 딸기 씨
            { 'n', 0x8BC34A }, // 멜론
            { 'N', 0x4E7D2A }, // 멜론 줄무늬
            { 'v', 0x6A3D8C }, // 가지
            { 'V', 0x8E5BB5 }, // 가지 윤기
            { 'u', 0xE07B26 }, // 호박
            { 'U', 0xB85E14 }, // 호박 골
            { 'h', 0xC8553D }, // 버섯 갓
            { 'H', 0xF1E3C8 }, // 버섯 대·점
            { 'w', 0xC3CCD5 }, // 스프링클러 금속
            { 'W', 0x6E7A86 }, // 스프링클러 대
            { 'q', 0x7CC4F0 }, // 물방울
            { 'c', 0xC98B4E }, // 스프링클러 Lv2 구리
            { 'C', 0xF2C94C }, // 스프링클러 Lv3 금
            { 'i', 0x8E6CCF }, // 루피너스 보라 (M4 Lupin Bloom)
            { 'I', 0x5B7FD6 }, // 루피너스 파랑
            { 'j', 0xD9CCF5 }, // 꽃봉오리·꽃잎
            { 'Q', 0x6FC3F7 }, // 스프링클러 범위 테두리 (M4 K23)
            { 'Z', 0xFFD54F }, // 스프링클러 설치·업그레이드 미리 보기 테두리 (M5 K9)
        };

        /// <summary>계절별 풀 색 (M2 K9): 봄, 여름, 가을, 겨울. [계절, 0=g / 1=G]</summary>
        static readonly int[,] Grass =
        {
            { 0x7CB65A, 0x5E9A44 },
            { 0x4E9A3A, 0x3B7F2C },
            { 0xC08A3E, 0xA06F2C },
            { 0xD5DCE2, 0xB7C1CA },
        };

        public static readonly string[] SoilDry =
        {
            "sssssssg",
            "SSSSSSSg",
            "sssssssg",
            "SSSSSSSg",
            "sssssssg",
            "SSSSSSSg",
            "sssssssg",
            "gggggggg",
        };

        public static readonly string[] SoilWet =
        {
            "mmmmmmmg",
            "MMMMMMMg",
            "mmmmmmmg",
            "MMMMMMMg",
            "mmmmmmmg",
            "MMMMMMMg",
            "mmmmmmmg",
            "gggggggg",
        };

        public static readonly string[] Locked =
        {
            "gggggggg",
            "gGgggGgg",
            "gggggggg",
            "ggggggGg",
            "gGgggggg",
            "gggggggg",
            "gggGgggg",
            "gggggggg",
        };

        static readonly string[] Seed =
        {
            "........",
            "........",
            "........",
            "..e.e...",
            "........",
            "...e....",
            "........",
            "........",
        };

        static readonly string[] Sprout =
        {
            "........",
            "........",
            "........",
            "..l.l...",
            "...l....",
            "...L....",
            "........",
            "........",
        };

        static readonly Dictionary<string, string[][]> crops = new Dictionary<string, string[][]>
        {
            {
                "turnip", new[]
                {
                    new[]
                    {
                        "........",
                        "........",
                        ".l...l..",
                        "..lLl...",
                        "...L....",
                        "..ttt...",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        ".L.L.L..",
                        "..LLL...",
                        "...L....",
                        "..ttt...",
                        ".ttttt..",
                        ".ppppp..",
                        "..ppp...",
                        "........",
                    },
                }
            },
            {
                "corn", new[]
                {
                    new[]
                    {
                        "........",
                        "...L....",
                        "..LL....",
                        "...LL...",
                        "..L.L...",
                        "...L....",
                        "...L....",
                        "........",
                    },
                    new[]
                    {
                        "...Y....",
                        "..LLy...",
                        ".L.Lyy..",
                        "...Lyy..",
                        "..LL.L..",
                        ".L.L....",
                        "...L....",
                        "........",
                    },
                }
            },
            {
                "carrot", new[]
                {
                    new[]
                    {
                        "........",
                        "........",
                        ".l.l.l..",
                        "..lLl...",
                        "...L....",
                        "...o....",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        ".L.L.L..",
                        "..LLL...",
                        "...L....",
                        "..ooo...",
                        "..oOo...",
                        "...o....",
                        "...o....",
                        "........",
                    },
                }
            },
            {
                "potato", new[]
                {
                    new[]
                    {
                        "........",
                        "..l.l...",
                        ".lLlLl..",
                        "..LLL...",
                        "...L....",
                        "........",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        ".L.L.L..",
                        "LLLLLLL.",
                        "..LLL...",
                        "...L....",
                        ".bb.bb..",
                        "bbb.bbb.",
                        ".b...b..",
                        "........",
                    },
                }
            },
            {
                "strawberry", new[]
                {
                    new[]
                    {
                        "........",
                        "........",
                        ".L.L.L..",
                        "..LLL...",
                        ".L.L.L..",
                        "...L....",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        "........",
                        ".L.L.L..",
                        "..LLL...",
                        ".kLLLk..",
                        "kKk.kKk.",
                        ".k...k..",
                        "........",
                        "........",
                    },
                }
            },
            {
                "tomato", new[]
                {
                    new[]
                    {
                        "...L....",
                        "..LL....",
                        "...LL...",
                        "..L.L...",
                        "...L....",
                        "...L....",
                        "...L....",
                        "........",
                    },
                    new[]
                    {
                        "...L....",
                        "..LLL...",
                        ".rL.Lr..",
                        "rrr.rrr.",
                        ".r.L.r..",
                        "...L....",
                        "...L....",
                        "........",
                    },
                }
            },
            {
                "melon", new[]
                {
                    new[]
                    {
                        "........",
                        "........",
                        "........",
                        ".l.l....",
                        "LLLLl...",
                        "...LL...",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        "........",
                        ".L......",
                        "..L.....",
                        "..nNnNn.",
                        ".nNnNnN.",
                        ".nNnNnN.",
                        "..nNnN..",
                        "........",
                    },
                }
            },
            {
                "eggplant", new[]
                {
                    new[]
                    {
                        "........",
                        "..L.L...",
                        "...L....",
                        "..LLL...",
                        "...L....",
                        "...v....",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        "..L.L...",
                        "...L....",
                        "..LLL...",
                        "..vLv...",
                        ".vVvVv..",
                        ".vvvvv..",
                        "..vvv...",
                        "........",
                    },
                }
            },
            {
                "pumpkin", new[]
                {
                    new[]
                    {
                        "........",
                        "........",
                        ".l...l..",
                        "LLL.LLL.",
                        "...L....",
                        "...u....",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        "...L....",
                        "..L.....",
                        ".uUuUu..",
                        "uUuUuUu.",
                        "uUuUuUu.",
                        ".uUuUu..",
                        "........",
                        "........",
                    },
                }
            },
            {
                "mushroom", new[]
                {
                    new[]
                    {
                        "........",
                        "........",
                        "........",
                        "...hh...",
                        "...HH...",
                        "...H....",
                        "........",
                        "........",
                    },
                    new[]
                    {
                        "........",
                        "..hhh...",
                        ".hHhHh..",
                        "hhhhhhh.",
                        "...H....",
                        "...H....",
                        "..HHH...",
                        "........",
                    },
                }
            },
        };

        static readonly string[][] fallback =
        {
            new[]
            {
                "........",
                "........",
                "..L.L...",
                "...L....",
                "...r....",
                "........",
                "........",
                "........",
            },
            new[]
            {
                "........",
                "..L.L...",
                "...L....",
                "..rrr...",
                ".rrrrr..",
                "..rrr...",
                "........",
                "........",
            },
        };

        /// <summary>스프링클러 Lv1~3 (M3). 레벨이 오를수록 머리가 크고 물줄기가 넓다.</summary>
        static readonly string[][] sprinklers =
        {
            new[]
            {
                "........",
                "...q....",
                "..qwq...",
                "...W....",
                "...W....",
                "..WWW...",
                "........",
                "........",
            },
            new[]
            {
                ".q...q..",
                "..q.q...",
                ".q.c.q..",
                "..cwc...",
                "...W....",
                "..WWW...",
                "........",
                "........",
            },
            new[]
            {
                "q..q..q.",
                ".q.q.q..",
                "q.CwC.q.",
                "..CwC...",
                "...W....",
                "..WWW...",
                "........",
                "........",
            },
        };

        public static string[] Sprinkler(int level)
        {
            if (level < 1) level = 1;
            if (level > sprinklers.Length) level = sprinklers.Length;
            return sprinklers[level - 1];
        }

        /// <summary>Lupin Bloom 장면 (04 S18, M4 K11): 새싹 → 꽃대 → 만개 → 갈아엎음. 마른 흙 위에 그린다.</summary>
        static readonly string[][] bloom =
        {
            new[]
            {
                "........",
                "........",
                "........",
                "........",
                "..l.l...",
                ".lLlLl..",
                "...L....",
                "........",
            },
            new[]
            {
                "...j....",
                "...l....",
                "..lLl...",
                "...L....",
                "..lLl...",
                ".lLLLl..",
                "..lLl...",
                "........",
            },
            new[]
            {
                "..j...j.",
                ".iIi.jI.",
                ".IiI.Ii.",
                ".iIi.iI.",
                ".IiI..L.",
                "..L..lL.",
                ".lLLlLl.",
                "........",
            },
            new[]
            {
                "SSSSSSS.",
                "sssisss.",
                "ssssssj.",
                "SSSSSSS.",
                "sjsssss.",
                "sssssis.",
                "SSSSSSS.",
                "........",
            },
        };

        public static string[] Bloom(int frame)
        {
            if (frame < 0) frame = 0;
            if (frame >= bloom.Length) frame = bloom.Length - 1;
            return bloom[frame];
        }

        /// <summary>stage: 0 씨앗, 1 새싹, 2 자라는 중, 3 다 자람.</summary>
        public static string[] Crop(string crop, int stage)
        {
            if (stage <= 0) return Seed;
            if (stage == 1) return Sprout;
            string[][] art;
            if (crop == null || !crops.TryGetValue(crop, out art)) art = fallback;
            return stage == 2 ? art[0] : art[1];
        }

        public static bool HasArt(string crop)
        {
            return crop != null && crops.ContainsKey(crop);
        }

        public static int Color(char c)
        {
            int v;
            return Palette.TryGetValue(c, out v) ? v : 0xFF00FF;
        }

        /// <summary>계절에 맞춘 색. 풀(g, G)만 계절에 따라 바뀐다.</summary>
        public static int Color(char c, int season)
        {
            if (season < 0 || season > 3) season = 0;
            if (c == 'g') return Grass[season, 0];
            if (c == 'G') return Grass[season, 1];
            return Color(c);
        }

        /// <summary>밤 색 (M2 K8): 모든 색에 factor를 곱한다.</summary>
        public static int Darken(int rgb, double factor)
        {
            int r = (int)(((rgb >> 16) & 0xFF) * factor);
            int g = (int)(((rgb >> 8) & 0xFF) * factor);
            int b = (int)((rgb & 0xFF) * factor);
            return (r << 16) | (g << 8) | b;
        }

        public const double NightFactor = 0.55;

        /// <summary>스프링클러 범위(스프링클러 + 물 받는 칸)의 바깥 선 색 (M4 K23).</summary>
        public const char RangeEdge = 'Q';

        /// <summary>설치·업그레이드 미리 보기 범위의 바깥 선 색 (M5 K9). 밤에도 어둡게 하지 않는다.</summary>
        public const char PreviewEdge = 'Z';
    }
}
