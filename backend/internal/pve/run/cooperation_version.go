package run

func cooperationVersion(version string) bool {
	return CooperationVersion(version)
}

func CooperationVersion(version string) bool {
	return version == "dc_ruins.v8.p5.1" || version == "dc_ruins.v7.p5.2" || version == "dc_ruins.v7.p5.1" || (version == "dc_ruins.v6.p5.1" || version == "dc_ruins.v6.p5.2") || version == "dc_ruins.v4.p3.1" || version == "dc_ruins.v4.p4.1" || version == "dc_ruins.v5.p4.1" || version == "dc_ruins.v5.p5.1"
}
