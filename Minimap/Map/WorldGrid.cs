using Unity.Mathematics;
using UnityEngine;

namespace MinimapHarmony;

public class WorldGrid<T>
{
	public T[] Cells;
	public float CellSize;
	public float CellSizeHalf;
	public float CellSizeInverse;
	public int CellCount;
	public int CellCountHalf;

	public T this[Vector3 worldPos]
	{
		get => this[WorldToGridCoords(worldPos)];
		set => this[WorldToGridCoords(worldPos)] = value;
	}

	public T this[int2 cellCoords]
	{
		get => this[cellCoords.x, cellCoords.y];
		set => this[cellCoords.x, cellCoords.y] = value;
	}

	public T this[int x, int y]
	{
		get => Cells[y * CellCount + x];
		set => Cells[y * CellCount + x] = value;
	}

	public WorldGrid(float gridSize, float cellSize)
	{
		CellSize = cellSize;
		CellSizeHalf = cellSize * 0.5f;
		CellSizeInverse = 1f / cellSize;
		CellCount = Mathf.CeilToInt(gridSize * CellSizeInverse);
		CellCountHalf = CellCount / 2;
		Cells = new T[CellCount * CellCount];
	}

	public int2 WorldToGridCoords(Vector3 worldPos)
	{
		int x = Mathf.CeilToInt(worldPos.x * CellSizeInverse);
		int z = Mathf.CeilToInt(worldPos.z * CellSizeInverse);
		x = Mathf.Clamp(x + CellCountHalf, 0, CellCount - 1);
		z = Mathf.Clamp(z + CellCountHalf, 0, CellCount - 1);
		return new int2(x, z);
	}

	public Vector3 GridToWorldCoords(int2 cellPos)
	{
		float x = (cellPos.x - CellCountHalf) * CellSize - CellSizeHalf;
		float z = (cellPos.y - CellCountHalf) * CellSize - CellSizeHalf;
		return new Vector3(x, 0f, z);
	}
}
