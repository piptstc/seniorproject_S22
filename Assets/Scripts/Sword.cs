using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Sword : MonoBehaviour
{

    private float startingPos;
    public float targetPos;
    public float attackRange = 0.5f;
    public KeyCode attackButton;
    public Transform attackPoint;
    public LayerMask enemyLayers;

    public int damage;

    void Start()
    {
        startingPos = transform.rotation.z;
    }

    void Update()
    {
        if (Input.GetKey(attackButton))
        {
            Attack();
        }
    }

    void OnTriggerEnter(Collider collider)
    {
        if (Input.GetKeyDown(attackButton))
        {
            if (collider.GetComponent<EnemyStats>())
            {
                EnemyStats stats = collider.GetComponent<EnemyStats>();
                stats.Hit(damage);
            }
        }
    }

    void Attack()
    {
        //add animation here

        Collider[] hitEnemies = Physics.OverlapSphere(attackPoint.position, attackRange, enemyLayers);

        foreach(Collider enemy in hitEnemies)
        {
            //Debug.Log("Hit enemy " + enemy.name);
            if (enemy.GetComponent<EnemyStats>())
            {
                EnemyStats stats = enemy.GetComponent<EnemyStats>();
                stats.Hit(damage);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!attackPoint) return;

        Gizmos.DrawWireSphere(attackPoint.position, attackRange);
    }

}