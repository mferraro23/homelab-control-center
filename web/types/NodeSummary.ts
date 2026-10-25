export type NodeSummary = {
	name: string;
	status: 'Ready' | 'NotReady' | 'Unknown';
	cpuCores: number;
	cpuUsedCores: number | null;
	memoryBytes: number;
	memoryUsedBytes: number | null;
};