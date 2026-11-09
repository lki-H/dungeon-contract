package main

import "errors"

func windowDimensions(runtime string, width, height int) (int, int, error) {
	if width == 0 && height == 0 {
		if runtime == "v5" {
			return 1536, 864, nil
		}
		return 1280, 720, nil
	}
	if width < 640 || width > 3840 || height < 360 || height > 2160 || width%16 != 0 || width*9 != height*16 {
		return 0, 0, errors.New("client window must be a bounded 16:9 size")
	}
	return width, height, nil
}
