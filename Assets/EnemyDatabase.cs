using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class EnemyDatabase
{
    public enum EnemyType { ENEMY_A, ENEMY_B, ENEMY_C, BOSS_1 };

    public struct EnemyData
    {
        public float maxHealth;
        public bool hasBarrier;
        public float maxBarrier;
    }




    public static EnemyData GetEnemyData(EnemyType type)
    {
        EnemyData enemyData = new EnemyData();
        switch (type)
        {
            case EnemyType.ENEMY_A:
                enemyData.maxHealth = 300;
                enemyData.hasBarrier = false;
                enemyData.maxBarrier = 0;
                break;
            case EnemyType.ENEMY_B:
                enemyData.maxHealth = 50;
                enemyData.hasBarrier = false;
                enemyData.maxBarrier = 0;
                break;
            case EnemyType.ENEMY_C:
                enemyData.maxHealth = 200;
                enemyData.hasBarrier = true;
                enemyData.maxBarrier = 50;
                break;
            case EnemyType.BOSS_1:
                enemyData.maxHealth = 500;
                enemyData.hasBarrier = true;
                enemyData.maxBarrier = 200;
                break;
        }

        return enemyData;
    }
}
