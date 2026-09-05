"""Validate the committed biome controls without network or a source survey download."""

from pathlib import Path
import unittest

from PIL import Image


class Landscapes(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        path = Path(__file__).resolve().parents[1] / "game/Assets/Planet/biomes.png"
        cls.image = Image.open(path)

    @classmethod
    def tearDownClass(cls):
        cls.image.close()

    def sample(self, latitude, longitude):
        return self.image.getpixel((int((longitude + 180) / 360 * self.image.width),
                                   int((90 - latitude) / 180 * self.image.height)))

    def test_global_layout(self):
        self.assertEqual(self.image.size, (4096, 2048))
        self.assertEqual(self.image.mode, "RGBA")

    def test_sahara_is_arid_and_treeless(self):
        vegetation, trees, aridity, ice = self.sample(25, 15)
        self.assertGreater(aridity, 230)
        self.assertLess(trees, 5)
        self.assertLess(vegetation, 20)
        self.assertEqual(ice, 0)

    def test_amazon_has_forest_without_ice_alpha(self):
        vegetation, trees, aridity, ice = self.sample(-4, -64)
        self.assertGreater(vegetation, 200)
        self.assertGreater(trees, 180)
        self.assertLess(aridity, 40)
        self.assertEqual(ice, 0)

    def test_polar_fallback_is_ice(self):
        self.assertEqual(self.sample(-80, 90), (0, 0, 0, 255))


if __name__ == "__main__":
    unittest.main()
