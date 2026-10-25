import {type NodeSummary} from '../types/NodeSummary'

function NodeCard({node}: {node: NodeSummary}) {
  return (
    <tr>
      <td>{node.Name}</td>
      <td>{node.Status}</td>
      <td>{node.CpuCores}</td>
      <td>{node.CpuUsedCores ?? 'N/A'}</td>
      <td>{(node.MemoryBytes / (1024 * 1024)).toFixed(2)} MB</td>
      <td>{node.MemoryUsedBytes !== null ? (node.MemoryUsedBytes / (1024 * 1024)).toFixed(2) + ' MB' : 'N/A'}</td>
    </tr>
  )
}

export default NodeCard